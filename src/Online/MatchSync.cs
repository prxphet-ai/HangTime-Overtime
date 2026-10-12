using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HarmonyLib;
using UnityEngine;

namespace HangtimeOvertime.Online
{
    // An online round. The host plays the real match; the client's copy of the scene is a mirror:
    //  - players and ball follow the host's snapshots (50 a second, shown a little behind so they can be interpolated);
    //    their physics is off, so the client's copy never scores, hits or blocks by itself;
    //  - what the host's game produced is replayed at the same moment: sounds, voices, dialogue lines, camera kicks,
    //    the ball's particle effects, jump dust, and each point (the client runs the game's own point routine, which
    //    updates its score, picks the next server and plays the point effects).
    internal static class MatchSync
    {
        public static int LoadSeq;                     // which round load the stream belongs to
        public static bool InMatch;                    // a Versus round scene is running (set up)
        public static bool Recording => Net.IsHost && Net.Connected && InMatch && VersusState.Active;
        public static bool Mirroring => Net.IsClient && InMatch && Mirror.Current != null;
        internal static bool Replay;                   // the client is playing a host event: let it through

        // The same four players on both machines: left hitter, the left's second player (bot setter or co-op partner),
        // right spiker, right setter.
        public static PlayerController[] Players()
        {
            var all = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var p = new PlayerController[4];
            p[0] = all.FirstOrDefault(Look.IsPlayerOne);
            p[1] = all.Where(x => x != p[0] && x.transform.position.x < 0f).OrderBy(x => x.setter ? 1 : 0).FirstOrDefault();
            var team = GameManager.Instance.currentTeam;
            var right = team != null ? team.GetComponentsInChildren<PlayerController>(true) : all.Where(x => x.transform.position.x > 0f).ToArray();
            p[2] = right.FirstOrDefault(x => x.GetComponent<SetterInput>() == null);
            p[3] = right.FirstOrDefault(x => x != p[2]);
            return p;
        }

        // ------------------------------------------------------------ host: recording

        private static readonly List<byte[]> pending = new List<byte[]>();
        private static NetWriter batch;
        private static int batchCount;

        internal static NetWriter Event(byte type)
        {
            // events are written into one batch per frame: [t][type][fields]
            if (batch == null) { batch = new NetWriter(Msg.Events).I(LoadSeq); batchCount = 0; }
            batchCount++;
            return batch.F(Time.unscaledTime).U8(type);
        }

        internal static void Flush()
        {
            if (batch == null) return;
            Net.Send(batch);
            batch = null;
        }

        public const byte EvSfx = 1, EvCam = 2, EvFx = 3, EvVoice = 4, EvLine = 5, EvJump = 6, EvPoint = 7;

        public static int IndexOf(PlayerController pc, PlayerController[] players)
        {
            for (int i = 0; i < players.Length; i++) if (players[i] == pc && pc != null) return i;
            return 255;
        }

        // ------------------------------------------------------------ client: setting up the mirror

        public static void SetupMirror(GameManager gm, GameObject team)
        {
            gm.matchLength = Generated.VersusRules.PointsPerRound;
            foreach (var ai in Object.FindObjectsByType<SpikerInput>(FindObjectsInactive.Include, FindObjectsSortMode.None)) ai.enabled = false;
            foreach (var ai in Object.FindObjectsByType<SetterInput>(FindObjectsInactive.Include, FindObjectsSortMode.None)) ai.enabled = false;
            foreach (var pc in Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var rb = pc.GetComponent<Rigidbody2D>();
                if (rb != null) rb.simulated = false;
            }
            var ball = Object.FindFirstObjectByType<BallMovement>();
            if (ball != null) ball.GetComponent<Rigidbody2D>().simulated = false;
            var players = Players();
            // the game registers the right side's server in the right spiker AI's first update, which the mirror switches off
            if (ball != null && players[2] != null) ball.opponent = players[2];
            Patches.VersusPatches.ServeTurns.Reset(players[2], VersusState.Setup.HumanSetter(1) ? players[3] : null);
            if (OnlineFlow.HostLook != null && players[0] != null) Look.Apply(Look.RigOf(players[0]), OnlineFlow.HostLook);
            var m = new GameObject("Overtime Online Mirror").AddComponent<Mirror>();
            m.Init(players, ball);
            InMatch = true;
            Net.Send(new NetWriter(Msg.Ready).I(LoadSeq));
            Plugin.Log.LogInfo($"Online: mirroring round {VersusState.Round} (load {LoadSeq}): " + string.Join(", ", players.Select(p => p != null ? p.name : "-")));
        }

        public static void SetupHost()
        {
            var go = new GameObject("Overtime Online Host Sync").AddComponent<HostSync>();
            go.Init(Players());
            InMatch = true;
        }
    }

    // ------------------------------------------------------------ host: snapshots and event batches

    internal class HostSync : MonoBehaviour
    {
        public static HostSync Current;
        public PlayerController[] Players;
        private BallMovement ball;
        private Rigidbody2D ballRb;
        private readonly List<(ParticleSystem ps, float time, bool playing)> effects = new List<(ParticleSystem, float, bool)>();
        private Animator[] anims;
        private static readonly AccessTools.FieldRef<PlayerController, Animator> animOf = AccessTools.FieldRefAccess<PlayerController, Animator>("animator");
        internal static readonly AccessTools.FieldRef<PlayerController, bool> servingOf = AccessTools.FieldRefAccess<PlayerController, bool>("serving");

        public void Init(PlayerController[] players)
        {
            Current = this;
            Players = players;
            anims = players.Select(p => p != null ? animOf(p) : null).ToArray();
            ball = FindFirstObjectByType<BallMovement>();
            ballRb = ball != null ? ball.GetComponent<Rigidbody2D>() : null;
            // the ball's particle effects, except the point ones (the client's own point routine plays those)
            if (ball != null)
                foreach (var f in AccessTools.GetDeclaredFields(typeof(BallMovement)).Where(f => f.FieldType == typeof(ParticleSystem) && !f.Name.ToLower().Contains("point")))
                {
                    var ps = (ParticleSystem)f.GetValue(ball);
                    if (ps != null) effects.Add((ps, ps.time, ps.isPlaying));
                }
        }

        private void OnDestroy() { if (Current == this) Current = null; MatchSync.InMatch = false; }

        private float lastDiag;

        private void FixedUpdate()
        {
            if (!MatchSync.Recording || ball == null) return;
            var gm = GameManager.Instance;
            if (Plugin.DevKeys.Value && Time.unscaledTime - lastDiag > 5f)
            {
                lastDiag = Time.unscaledTime;
                Plugin.Log.LogInfo("[diag] host: " + string.Join(", ", Players.Where(p => p != null).Select(p => $"{p.name} x{p.transform.position.x:F1} srv{(servingOf(p) ? 1 : 0)} cs{p.COURT_SIZE:F1}")) +
                                   " | pads " + string.Join(", ", Net.RemotePads.Select(kv => $"{kv.Key}:{kv.Value.Count[0]}/{kv.Value.X:F1}")));
            }
            var w = new NetWriter(Msg.Snap).I(MatchSync.LoadSeq).F(Time.unscaledTime)
                .V(ball.transform.position).V(ballRb.linearVelocity)
                .U8(gm.playerPoints).U8(gm.opponentPoints).U8(BallMovement.lastTouched + 1).U8(Players.Length);
            for (int i = 0; i < Players.Length; i++)
            {
                var p = Players[i];
                bool on = p != null && p.isActiveAndEnabled;
                w.B(on);
                if (!on) continue;
                var rb = p.GetComponent<Rigidbody2D>();
                var a = anims[i];
                int hash = 0; float t = 0f;
                if (a != null && a.isActiveAndEnabled)
                {
                    var st = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0) : a.GetCurrentAnimatorStateInfo(0);
                    hash = st.fullPathHash; t = st.normalizedTime;
                }
                w.V(p.transform.position).V(rb.linearVelocity).I(hash).F(t).B(servingOf(p));
            }
            Net.Send(w, false);
        }

        private void LateUpdate()
        {
            if (!MatchSync.Recording) { MatchSync.Flush(); return; }
            // a ball effect that (re)started since last frame
            for (int i = 0; i < effects.Count; i++)
            {
                var (ps, last, was) = effects[i];
                if (ps == null) continue;
                bool now = ps.isPlaying;
                if (now && (!was || ps.time < last)) MatchSync.Event(MatchSync.EvFx).U8(i);
                effects[i] = (ps, ps.time, now);
            }
            MatchSync.Flush();
        }
    }

    // ------------------------------------------------------------ client: the mirror

    internal class Mirror : MonoBehaviour
    {
        public static Mirror Current;
        private PlayerController[] players;
        private Rigidbody2D[] bodies;
        private Animator[] anims;
        private BallMovement ball;
        private Rigidbody2D ballRb;
        private readonly List<ParticleSystem> effects = new List<ParticleSystem>();
        private static readonly AccessTools.FieldRef<PlayerController, Animator> animOf = AccessTools.FieldRefAccess<PlayerController, Animator>("animator");
        private static readonly AccessTools.FieldRef<BallMovement, float> ballSideOf = AccessTools.FieldRefAccess<BallMovement, float>("ballSide");
        private static readonly AccessTools.FieldRef<BallMovement, bool> giveAceOf = AccessTools.FieldRefAccess<BallMovement, bool>("giveAce");
        private static readonly AccessTools.FieldRef<BallMovement, RandomSound> voiceOf = AccessTools.FieldRefAccess<BallMovement, RandomSound>("humanSounds");
        private static readonly AccessTools.FieldRef<BallMovement, OpponentDialogue> dialogueOf = AccessTools.FieldRefAccess<BallMovement, OpponentDialogue>("opponentDialogue");

        private struct PlayerSnap { public bool On; public Vector2 Pos, Vel; public int Hash; public float T; public bool Serving; }
        private class Snap { public float T; public Vector2 BallPos, BallVel; public int P, O, LastTouched; public PlayerSnap[] Players; }
        private readonly List<Snap> snaps = new List<Snap>();
        private readonly Queue<(float t, NetReader r, byte type)> events = new Queue<(float, NetReader, byte)>();
        private bool haveClock;
        private readonly List<float> recent = new List<float>(), sorted = new List<float>();
        public int Starved;                                         // frames shown past the newest snapshot (diagnostics)
        private float offset;                                       // host time - local time (newest-arriving estimate)
        private float scoreMismatchSince = -1f;
        private float renderTime;

        public void Init(PlayerController[] p, BallMovement b)
        {
            Current = this;
            players = p;
            bodies = p.Select(x => x != null ? x.GetComponent<Rigidbody2D>() : null).ToArray();
            anims = p.Select(x => x != null ? animOf(x) : null).ToArray();
            ball = b;
            ballRb = b != null ? b.GetComponent<Rigidbody2D>() : null;
            if (b != null)
                foreach (var f in AccessTools.GetDeclaredFields(typeof(BallMovement)).Where(f => f.FieldType == typeof(ParticleSystem) && !f.Name.ToLower().Contains("point")))
                {
                    var ps = (ParticleSystem)f.GetValue(b);
                    if (ps != null) effects.Add(ps);
                }
        }

        private void OnDestroy() { if (Current == this) Current = null; MatchSync.InMatch = false; }

        // ---- receiving

        public void OnSnap(NetReader r)
        {
            float t = r.F();
            var s = new Snap { T = t, BallPos = r.V(), BallVel = r.V(), P = r.U8(), O = r.U8(), LastTouched = r.U8() - 1 };
            int n = r.U8();
            s.Players = new PlayerSnap[n];
            for (int i = 0; i < n; i++)
            {
                if (!r.B()) continue;
                s.Players[i] = new PlayerSnap { On = true, Pos = r.V(), Vel = r.V(), Hash = r.I(), T = r.F(), Serving = r.B() };
            }
            // where the host's clock is, from recent arrivals: a high percentile, so one unusually quick packet can't make
            // the picture run ahead of the snapshots it has (it would hold still until the next one: a freeze)
            recent.Add(t - Time.unscaledTime);
            if (recent.Count > 60) recent.RemoveAt(0);
            sorted.Clear(); sorted.AddRange(recent); sorted.Sort();
            float target = sorted[Mathf.Clamp(Mathf.RoundToInt(sorted.Count * 0.75f), 0, sorted.Count - 1)];
            offset = haveClock ? Mathf.Lerp(offset, target, 0.1f) : target;
            haveClock = true;
            if (snaps.Count > 0 && t <= snaps[snaps.Count - 1].T) return;                          // late or duplicate
            snaps.Add(s);
            while (snaps.Count > 120) snaps.RemoveAt(0);
        }

        public void OnEvents(NetReader r)
        {
            while (r.More)
            {
                float t = r.F();
                byte type = (byte)r.U8();
                // the batch reader moves on: each event keeps its own copy of its fields until it plays
                events.Enqueue((t, Read(type, r), type));
            }
        }

        // copies one event's fields into its own reader (the batch reader moves on)
        private static NetReader Read(byte type, NetReader r)
        {
            var w = new NetWriter(Msg.Events);
            switch (type)
            {
                case MatchSync.EvSfx: w.S(r.S()).F(r.F()).F(r.F()).F(r.F()); break;
                case MatchSync.EvCam: w.U8(r.U8()).F(r.F()); break;
                case MatchSync.EvFx: w.U8(r.U8()); break;
                case MatchSync.EvVoice: break;
                case MatchSync.EvLine: w.S(r.S()).F(r.F()); break;
                case MatchSync.EvJump: w.U8(r.U8()); break;
                case MatchSync.EvPoint: w.F(r.F()).V(r.V()).V(r.V()).F(r.F()).I(r.I()).I(r.I()).B(r.B()); break;
                default: FxSync.Copy(type, r, w); break;
            }
            return new NetReader(w.Bytes());
        }

        // ---- playing back

        private float Delay => Plugin.OnlineBuffer.Value / 1000f;

        private void Update()
        {
            if (!haveClock) return;
            renderTime = Time.unscaledTime + offset - Delay;
            while (events.Count > 0 && events.Peek().t <= renderTime)
            {
                var (_, r, type) = events.Dequeue();
                MatchSync.Replay = true;
                try { Play(type, r); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Online: event {type} failed: {e.Message}"); }
                finally { MatchSync.Replay = false; }
            }
            Apply(false);
            if (Patches.VersusDriver.Autopilot && !Patches.VersusOverlay.IsMirror && !GameManager.Instance.done) DriveOwnPlayers();
        }

        private void LateUpdate() => Apply(true);

        // development autopilot: this machine's players play themselves, sent to the host like key presses
        private readonly Patches.AutoPilot pilot = new Patches.AutoPilot();
        private float lastDiag;
        private void DriveOwnPlayers()
        {
            var setup = VersusState.Setup;
            if (setup == null) return;
            if (Time.unscaledTime - lastDiag > 5f)
            {
                lastDiag = Time.unscaledTime;
                Plugin.Log.LogInfo("[diag] client: " + string.Join(", ", setup.AllHumans.Where(h => h.Input?.Kind == InputKind.Remote)
                    .Select(h => { var pc = PlayerOf(h); return $"P{h.Number} key{h.Input.DeviceId} {pc?.name} x{pc?.transform.position.x:F1} srv{(pc != null && pc.IsServing() ? 1 : 0)} gnd{(pc != null && pc.getGrounded() ? 1 : 0)}"; })));
            }
            var mine = setup.AllHumans.Where(h => h.Input?.Kind == InputKind.Remote).ToList();
            var mates = mine.Select(PlayerOf).Where(p => p != null).ToList();
            foreach (var slot in mine)
            {
                var pc = PlayerOf(slot);
                if (pc != null) Net.OverridePad(slot.Input.DeviceId, pilot.Pad(pc, mates));
            }
        }

        private PlayerController PlayerOf(VersusSlot s) =>
            players[s.Side == 0 ? (s.Role == "partner" ? 1 : 0) : (s.Role == "setter" ? 3 : 2)];

        private void Apply(bool transforms)
        {
            if (snaps.Count == 0) return;
            // the two snapshots around the render time (or the newest, held)
            Snap a = snaps[0], b = snaps[0];
            for (int i = snaps.Count - 1; i >= 0; i--)
                if (snaps[i].T <= renderTime) { a = snaps[i]; b = i + 1 < snaps.Count ? snaps[i + 1] : snaps[i]; break; }
            float k = b.T > a.T ? Mathf.Clamp01((renderTime - a.T) / (b.T - a.T)) : 0f;
            // past the newest snapshot (a late packet): keep everything moving along its velocity for a moment
            float ahead = a == b && renderTime > a.T ? Mathf.Min(renderTime - a.T, 0.12f) : 0f;
            if (ahead > 0f && transforms) Starved++;
            if (ball != null)
            {
                var vel = Vector2.Lerp(a.BallVel, b.BallVel, k);
                var pos = Vector2.Lerp(a.BallPos, b.BallPos, k) + vel * ahead;
                ballRb.linearVelocity = vel;
                if (transforms) ball.transform.position = new Vector3(pos.x, pos.y, ball.transform.position.z);
                BallMovement.lastTouched = b.LastTouched;
            }
            for (int i = 0; i < players.Length && i < a.Players.Length; i++)
            {
                var p = players[i];
                if (p == null || !a.Players[i].On) continue;
                var pa = a.Players[i];
                var pb = i < b.Players.Length && b.Players[i].On ? b.Players[i] : pa;
                bodies[i].linearVelocity = Vector2.Lerp(pa.Vel, pb.Vel, k);
                HostSync.servingOf(p) = pb.Serving;
                if (transforms)
                {
                    var pos = Vector2.Lerp(pa.Pos, pb.Pos, k) + Vector2.Lerp(pa.Vel, pb.Vel, k) * ahead;
                    if (ahead > 0f) pos.y = Mathf.Max(pos.y, Mathf.Min(pa.Pos.y, pb.Pos.y));   // not through the floor
                    p.transform.position = new Vector3(pos.x, pos.y, p.transform.position.z);
                    var an = anims[i];
                    if (an != null && an.isActiveAndEnabled && pb.Hash != 0)
                    {
                        var cur = an.IsInTransition(0) ? an.GetNextAnimatorStateInfo(0) : an.GetCurrentAnimatorStateInfo(0);
                        if (cur.fullPathHash != pb.Hash) an.Play(pb.Hash, 0, pb.T);
                    }
                }
            }
            // the score follows the host's (normally the replayed points keep it equal already)
            var gm = GameManager.Instance;
            if (transforms && gm != null)
            {
                if (gm.playerPoints != b.P || gm.opponentPoints != b.O)
                {
                    if (scoreMismatchSince < 0f) scoreMismatchSince = Time.unscaledTime;
                    else if (Time.unscaledTime - scoreMismatchSince > 1.5f)
                    {
                        Plugin.Log.LogInfo($"Online: score corrected {gm.playerPoints}-{gm.opponentPoints} -> {b.P}-{b.O}");
                        gm.playerPoints = b.P; gm.opponentPoints = b.O;
                        scoreMismatchSince = -1f;
                    }
                }
                else scoreMismatchSince = -1f;
            }
        }

        private void Play(byte type, NetReader r)
        {
            if (FxSync.Play(type, r, players, ball)) { if (Plugin.DevKeys.Value) Plugin.Log.LogInfo($"[fx] client drew event {type}"); return; }
            switch (type)
            {
                case MatchSync.EvSfx:
                {
                    string name = r.S(); float vol = r.F(), change = r.F(), pitch = r.F();
                    var clip = Clips.Find(name);
                    if (clip != null) AudioManager.Instance.PlaySFX(clip, vol, change, pitch);
                    break;
                }
                case MatchSync.EvCam:
                {
                    int kind = r.U8(); float amount = r.F();
                    if (kind == 0) CameraController.zoom(amount);
                    else if (kind == 1) CameraController.freezeTime(amount);
                    else if (kind == 2) CameraController.Shake(amount);
                    else CameraController.Rotate(amount);
                    break;
                }
                case MatchSync.EvFx:
                {
                    int i = r.U8();
                    if (i < effects.Count && effects[i] != null) effects[i].Play();
                    break;
                }
                case MatchSync.EvVoice:
                    if (ball != null) voiceOf(ball)?.PlaySound();
                    break;
                case MatchSync.EvLine:
                {
                    string line = r.S(); float pitch = r.F();
                    if (ball != null) dialogueOf(ball)?.PlayDialogue(line, pitch);
                    break;
                }
                case MatchSync.EvJump:
                {
                    int i = r.U8();
                    if (i < players.Length && players[i] != null) GameManager.Instance.particles.PlayJumpEffect(players[i].transform, bodies[i]);
                    break;
                }
                case MatchSync.EvPoint:
                {
                    float dir = r.F(); var pos = r.V(); var vel = r.V();
                    if (ball == null) break;
                    ball.transform.position = new Vector3(pos.x, pos.y, ball.transform.position.z);
                    ballRb.linearVelocity = vel;
                    ballSideOf(ball) = r.F();
                    BallMovement.lastTouched = r.I();
                    BallMovement.touchCounter = r.I();
                    giveAceOf(ball) = r.B();
                    BallMovement.rallyDone = false;
                    ball.StartCoroutine(ball.GetPoint(dir));
                    break;
                }
            }
        }
    }

    // Sound clips by name (the host names the clip, the client plays its own copy of it).
    internal static class Clips
    {
        private static Dictionary<string, AudioClip> byName;
        public static AudioClip Find(string name)
        {
            if (byName == null || (byName.TryGetValue(name, out var c0) && c0 == null))
            {
                byName = new Dictionary<string, AudioClip>();
                foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>()) if (c != null && !byName.ContainsKey(c.name)) byName[c.name] = c;
            }
            return byName.TryGetValue(name, out var clip) ? clip : null;
        }
    }

    // ------------------------------------------------------------ the game hooks: record on the host, hold back on the client

    internal static class SyncPatches
    {
        // the client's copy only makes these when replaying the host
        private static bool Hold => MatchSync.Mirroring && !MatchSync.Replay;

        [HarmonyPatch(typeof(AudioManager), "PlaySFX")]
        private static class Sfx
        {
            [HarmonyPrefix]
            private static bool Prefix(AudioClip clip, float volume, float pitchChange, float pitch)
            {
                if (MatchSync.Recording && clip != null) MatchSync.Event(MatchSync.EvSfx).S(clip.name).F(volume).F(pitchChange).F(pitch);
                return !Hold;
            }
        }

        [HarmonyPatch(typeof(CameraController), "zoom")]
        private static class CamZoom { [HarmonyPrefix] private static bool Prefix(float inputZoom) => Cam(0, inputZoom); }

        [HarmonyPatch(typeof(CameraController), "freezeTime")]
        private static class CamFreeze { [HarmonyPrefix] private static bool Prefix(float amount) => Cam(1, amount); }

        [HarmonyPatch(typeof(CameraController), "Shake")]
        private static class CamShake { [HarmonyPrefix] private static bool Prefix(float amount) => Cam(2, amount); }

        [HarmonyPatch(typeof(CameraController), "Rotate")]
        private static class CamRotate { [HarmonyPrefix] private static bool Prefix(float amount) => Cam(3, amount); }

        private static bool Cam(int kind, float amount)
        {
            if (MatchSync.Recording) MatchSync.Event(MatchSync.EvCam).U8(kind).F(amount);
            return !Hold;
        }

        [HarmonyPatch(typeof(RandomSound), "PlaySound")]
        private static class Voice
        {
            [HarmonyPrefix]
            private static bool Prefix(RandomSound __instance)
            {
                if (__instance.GetComponentInParent<BallMovement>() == null) return true;   // only the ball's voices travel
                if (MatchSync.Recording) MatchSync.Event(MatchSync.EvVoice);
                return !Hold;
            }
        }

        [HarmonyPatch(typeof(OpponentDialogue), "PlayDialogue")]
        private static class Line
        {
            [HarmonyPrefix]
            private static bool Prefix(string dialogue, float pitch)
            {
                if (MatchSync.Recording) MatchSync.Event(MatchSync.EvLine).S(dialogue).F(pitch);
                return !Hold;
            }
        }

        [HarmonyPatch(typeof(ParticleManager), "PlayJumpEffect")]
        private static class JumpDust
        {
            [HarmonyPrefix]
            private static bool Prefix(Transform playerTransform)
            {
                if (MatchSync.Recording && HostSync.Current != null)
                    MatchSync.Event(MatchSync.EvJump).U8(MatchSync.IndexOf(playerTransform.GetComponent<PlayerController>(), HostSync.Current.Players));
                return !Hold;
            }
        }

        private static readonly AccessTools.FieldRef<BallMovement, float> ballSideOf = AccessTools.FieldRefAccess<BallMovement, float>("ballSide");
        private static readonly AccessTools.FieldRef<BallMovement, bool> giveAceOf = AccessTools.FieldRefAccess<BallMovement, bool>("giveAce");

        [HarmonyPatch(typeof(BallMovement), "GetPoint")]
        private static class Point
        {
            [HarmonyPrefix]
            private static void Prefix(BallMovement __instance, float direction)
            {
                if (!MatchSync.Recording) return;
                var rb = __instance.GetComponent<Rigidbody2D>();
                MatchSync.Event(MatchSync.EvPoint).F(direction).V(__instance.transform.position).V(rb.linearVelocity)
                    .F(ballSideOf(__instance)).I(BallMovement.lastTouched).I(BallMovement.touchCounter).B(giveAceOf(__instance));
            }
        }

        // the client's ball never lands or touches anything by itself (its physics is off; this is a second guard)
        [HarmonyPatch(typeof(BallMovement), "OnCollisionEnter2D")]
        private static class NoLocalTouch { [HarmonyPrefix] private static bool Prefix() => !MatchSync.Mirroring; }
    }
}

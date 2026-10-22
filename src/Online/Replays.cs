using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HangtimeOvertime.Patches;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Online
{
    // Replays. The game isn't deterministic (Unity physics, the AI's and the perks' random rolls), so inputs + a seed
    // would drift apart within seconds; a replay is the match stream the online host already sends (50 snapshots a
    // second of the ball and the four players with their animations, and timed events: sounds, camera, effects, voice,
    // lines, points), written to a file and played back by the same mirror a friend's PC uses, on a replay clock.
    // Recorded: every Versus round (local, online host, online friend) and every Classic / Co-op / Infinite match.
    internal class ReplayHeader
    {
        public int Version = 1;
        public string Kind = "versus";            // versus | run
        public string Title = "", Date = "", Format = "1v1", Team0 = "", Team1 = "", Look = "", Left = "", Right = "";
        public int GameNumber = 1, Players = 1, Points = 5, ScoreL, ScoreR;
        public float Seconds;
    }

    internal static class Replays
    {
        public static bool Capturing => rec != null;
        public static bool Playing;
        public static ReplayHeader Current;                       // the header of the replay being played
        public static List<byte[]> CurrentMsgs;
        private static ReplayHeader recHeader;
        private static List<byte[]> rec;
        public const int KeepRecent = 10;
        private static string Dir => Path.Combine(Application.persistentDataPath, "HangtimeOvertime_replays");

        // ------------------------------------------------------------ recording

        public static void Begin(ReplayHeader h)
        {
            if (!Plugin.RecordReplays.Value || Playing) return;
            recHeader = h;
            recHeader.Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            rec = new List<byte[]>(8192);
            Plugin.Log.LogInfo("Replay: recording " + h.Title);
        }

        public static void Add(byte[] msg) { if (rec != null && msg != null) rec.Add(msg); }

        // an online friend: the host's stream as it arrives
        public static void Incoming(NetReader r) { if (rec != null && r.Raw != null) rec.Add(r.Raw); }

        // the round / match scene ends: the replay goes to the recent ones (short ones are dropped)
        public static void End()
        {
            if (rec == null) return;
            var msgs = rec; var h = recHeader;
            rec = null; recHeader = null;
            var snaps = msgs.Where(m => m.Length > 9 && m[0] == (byte)Msg.Snap).ToList();
            if (snaps.Count < 150) { Plugin.Log.LogInfo($"Replay: too short ({snaps.Count} snapshots), not kept"); return; }
            float t0 = SnapTime(snaps[0]), t1 = SnapTime(snaps[snaps.Count - 1]);
            h.Seconds = t1 - t0;
            var (p, o) = SnapScore(snaps[snaps.Count - 1]);
            h.ScoreL = p; h.ScoreR = o;
            try
            {
                Directory.CreateDirectory(Dir);
                string file = Path.Combine(Dir, $"recent-{DateTime.Now:yyyyMMdd-HHmmss-fff}.ovr");
                Write(file, h, msgs);
                foreach (var old in Directory.GetFiles(Dir, "recent-*.ovr").OrderByDescending(f => f).Skip(KeepRecent)) File.Delete(old);
                Plugin.Log.LogInfo($"Replay: kept {Path.GetFileName(file)} ({h.Seconds:F0} s, {msgs.Count} messages, {new FileInfo(file).Length / 1024} KB)");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Replay: couldn't write: " + e.Message); }
        }

        private static float SnapTime(byte[] m) => BitConverter.ToSingle(m, 5);                // [id][seq int][t float]
        private static (int, int) SnapScore(byte[] m) => (m[1 + 4 + 4 + 16], m[1 + 4 + 4 + 16 + 1]);   // after ball pos + vel

        // ------------------------------------------------------------ files

        public class Entry { public string Path; public bool Saved; public ReplayHeader Header; }

        public static List<Entry> List()
        {
            var l = new List<Entry>();
            if (!Directory.Exists(Dir)) return l;
            foreach (var f in Directory.GetFiles(Dir, "*.ovr").OrderByDescending(File.GetLastWriteTimeUtc))
            {
                var h = ReadHeader(f);
                if (h != null) l.Add(new Entry { Path = f, Saved = Path.GetFileName(f).StartsWith("saved-"), Header = h });
            }
            return l;
        }

        public static string Keep(Entry e)
        {
            string to = Path.Combine(Dir, "saved-" + Path.GetFileName(e.Path).Replace("recent-", ""));
            File.Move(e.Path, to);
            Plugin.Log.LogInfo("Replay: saved " + Path.GetFileName(to));
            return to;
        }

        public static void Delete(Entry e) { File.Delete(e.Path); Plugin.Log.LogInfo("Replay: deleted " + Path.GetFileName(e.Path)); }

        private static void Write(string file, ReplayHeader h, List<byte[]> msgs)
        {
            using (var fs = File.Create(file))
            using (var gz = new GZipStream(fs, CompressionMode.Compress))
            using (var w = new BinaryWriter(gz, Encoding.UTF8))
            {
                w.Write("OVTR"); WriteHeader(w, h);
                w.Write(msgs.Count);
                foreach (var m in msgs) { w.Write(m.Length); w.Write(m); }
            }
        }

        private static void WriteHeader(BinaryWriter w, ReplayHeader h)
        {
            w.Write(h.Version); w.Write(h.Kind); w.Write(h.Title); w.Write(h.Date); w.Write(h.Format); w.Write(h.Team0); w.Write(h.Team1);
            w.Write(h.Look ?? ""); w.Write(h.Left); w.Write(h.Right); w.Write(h.GameNumber); w.Write(h.Players); w.Write(h.Points);
            w.Write(h.ScoreL); w.Write(h.ScoreR); w.Write(h.Seconds);
        }

        private static ReplayHeader ReadHeaderFrom(BinaryReader r)
        {
            if (r.ReadString() != "OVTR") return null;
            var h = new ReplayHeader { Version = r.ReadInt32() };
            if (h.Version != 1) return null;
            h.Kind = r.ReadString(); h.Title = r.ReadString(); h.Date = r.ReadString(); h.Format = r.ReadString(); h.Team0 = r.ReadString(); h.Team1 = r.ReadString();
            h.Look = r.ReadString(); h.Left = r.ReadString(); h.Right = r.ReadString(); h.GameNumber = r.ReadInt32(); h.Players = r.ReadInt32(); h.Points = r.ReadInt32();
            h.ScoreL = r.ReadInt32(); h.ScoreR = r.ReadInt32(); h.Seconds = r.ReadSingle();
            return h;
        }

        private static ReplayHeader ReadHeader(string file)
        {
            try
            {
                using (var fs = File.OpenRead(file))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                using (var r = new BinaryReader(gz, Encoding.UTF8)) return ReadHeaderFrom(r);
            }
            catch { return null; }
        }

        public static bool Load(string file)
        {
            try
            {
                using (var fs = File.OpenRead(file))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                using (var r = new BinaryReader(gz, Encoding.UTF8))
                {
                    var h = ReadHeaderFrom(r);
                    if (h == null) return false;
                    int n = r.ReadInt32();
                    var msgs = new List<byte[]>(n);
                    for (int i = 0; i < n; i++) msgs.Add(r.ReadBytes(r.ReadInt32()));
                    Current = h; CurrentMsgs = msgs;
                    return true;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Replay: couldn't read " + file + ": " + e.Message); return false; }
        }

        // ------------------------------------------------------------ headers for what's being played

        public static ReplayHeader VersusHeader(string look)
        {
            var s = VersusState.Setup;
            string Side(int side) => $"{s.SideLabel(side)} · {VersusState.TeamName(side, s.Team[side]).ToUpperInvariant()}";
            return new ReplayHeader
            {
                Kind = "versus", Format = s.Format, Team0 = s.Team[0] ?? "", Team1 = s.Team[1] ?? "", Look = look ?? "", GameNumber = 1,
                Players = GameManager.Instance.numberOfPlayers, Points = LobbyRules.Current.Points, Left = Side(0), Right = Side(1),
                Title = $"VERSUS {s.Format.Replace("v", " VS ").ToUpperInvariant()}  ·  ROUND {VersusState.Round}" + (Net.Online ? "  ·  ONLINE" : ""),
            };
        }

        public static ReplayHeader RunHeader(GameManager gm)
        {
            string mode = RunState.Mode == RunMode.Infinite ? (RunState.Daily != null ? "DAILY" : "INFINITE") + $" ROUND {RunState.InfiniteMatch}" :
                          RunState.Draft != null ? "DRAFT CUP" : Daily.Cup ? "DAILY CUP" : RunState.Mode == RunMode.Loop ? $"LOOP {RunState.Loop}" :
                          gm.numberOfPlayers == 2 ? "CO-OP" : "CLASSIC";
            string opp = gm.opponentTeam != null ? gm.opponentTeam.teamName : "?";
            return new ReplayHeader
            {
                Kind = "run", Format = gm.numberOfPlayers == 2 ? "2v2" : "1v1", Team0 = RunState.Draft ?? "", Team1 = TeamRoster.CurrentKey ?? (gm.currentTeam != null ? gm.currentTeam.name.Replace("(Clone)", "").Trim() : ""),
                Look = JsonUtility.ToJson(Look.Current), GameNumber = GameManager.gameNumber, Players = gm.numberOfPlayers, Points = gm.matchLength,
                Left = "YOU" + (gm.numberOfPlayers == 2 ? " (CO-OP)" : ""), Right = opp.ToUpperInvariant(),
                Title = $"{mode}  ·  MATCH {GameManager.gameNumber}  ·  VS {opp.ToUpperInvariant()}",
            };
        }

        // ------------------------------------------------------------ watching

        // from the replays menu: the replay's scene as a Versus round that only mirrors
        public static void Watch(Entry e)
        {
            if (!Load(e.Path)) return;
            var h = Current;
            var inputs = Enumerable.Range(0, 4).Select(_ => (SlotInput)null).ToList();
            var setup = VersusSetup.Create(h.Format, inputs, h.Team0, h.Team1);
            foreach (var s in setup.Slots) s.Control = SlotControl.AI;
            VersusState.NewMatch(setup);
            LobbyRules.Current.Points = Mathf.Clamp(h.Points, 1, 15);
            Playing = true;
            Plugin.Log.LogInfo($"Replay: watching {h.Title} ({h.Seconds:F0} s)");
            VersusMenu.StartScene(h.Players);
        }

        public static void Stop()
        {
            if (!Playing) return;
            Playing = false;
            Current = null; CurrentMsgs = null;
            LobbyRules.Restore();
            Time.timeScale = 1f;
            VersusFlow.ToMenu();
        }
    }

    // plays a loaded replay through the mirror: the replay clock (pause, speed, seeking), events as the clock passes them
    internal class ReplayPlayer : MonoBehaviour
    {
        public static ReplayPlayer Current;
        private Mirror mirror;
        private readonly List<(float t, byte[] m)> events = new List<(float, byte[])>();
        private readonly List<(float t, int p, int o)> scores = new List<(float, int, int)>();
        private float t0, t1, clock;
        private int next;
        public bool Paused;
        public bool AtEnd;                                         // stopped by reaching the end (seeking back plays on)
        public float Speed = 1f;
        public float Position => clock - t0;
        public float Length => t1 - t0;

        public void Init(Mirror m)
        {
            Current = this;
            mirror = m;
            mirror.ReplayMode = true;
            foreach (var bytes in Replays.CurrentMsgs)
            {
                var r = new NetReader(bytes);
                r.I();                                                             // the round load it belonged to
                if (r.Id == Msg.Snap)
                {
                    var t = BitConverter.ToSingle(bytes, 5);
                    mirror.OnSnap(r);
                    scores.Add((t, bytes[25], bytes[26]));
                }
                else if (r.Id == Msg.Events && bytes.Length > 9) events.Add((BitConverter.ToSingle(bytes, 5), bytes));
            }
            t0 = scores.Count > 0 ? scores[0].t : 0f; t1 = scores.Count > 0 ? scores[scores.Count - 1].t : 0f;
            clock = t0;
            mirror.ReplayTime = clock;
            gameObject.AddComponent<ReplayHud>();
        }

        private void OnDestroy() { if (Current == this) Current = null; }

        private void Update()
        {
            if (!Paused) clock = Mathf.Min(t1, clock + Time.unscaledDeltaTime * Speed);
            if (clock >= t1 && !Paused) { Paused = true; AtEnd = true; }
            while (next < events.Count && events[next].t <= clock)
            {
                var r = new NetReader(events[next].m); r.I();
                mirror.OnEvents(r);
                next++;
            }
            mirror.ReplayTime = clock;
        }

        // jump to a moment: events before it are skipped (sounds, camera), the score is set from the snapshot there
        public void Seek(float position)
        {
            clock = Mathf.Clamp(t0 + position, t0, t1);
            mirror.ClearEvents();
            next = events.FindIndex(e => e.t > clock);
            if (next < 0) next = events.Count;
            var s = scores.LastOrDefault(x => x.t <= clock);
            var gm = GameManager.Instance;
            if (gm != null) { gm.playerPoints = s.p; gm.opponentPoints = s.o; }
            if (AtEnd && clock < t1) { AtEnd = false; Paused = false; }
        }
    }

    // the viewer's controls: play / pause, speed, seek, camera, exit
    internal class ReplayHud : OvertimePanel
    {
        public static float SpeedNow => ReplayPlayer.Current == null ? 1f : ReplayPlayer.Current.Paused ? 0f : ReplayPlayer.Current.Speed;
        private static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f, 4f };
        public static int CameraMode;                              // 0 the game's, 1 the whole court, 2 follow the ball
        private static readonly string[] Cams = { "GAME", "WHOLE COURT", "FOLLOW BALL" };
        private TextMeshPro time, info;
        private SpriteRenderer bar, fill;
        private float barW;

        private void Start()
        {
            if (!InitKit(250)) { Destroy(this); return; }
            var h = Replays.Current;
            float top = H / 2f, bottom = -H / 2f;
            Box("Top", new Vector2(0f, top - 1.4f), new Vector2(W * 0.7f, 2.2f), new Color(0.02f, 0.12f, 0.22f, 0.75f), 1);
            Text($"<color=#FFD95A>REPLAY</color>  ·  <noparse>{h.Title}</noparse>  ·  {h.Date}", new Vector2(0f, top - 1f), 0.5f, W * 0.68f, Color.white);
            Text($"<noparse>{h.Left}  VS  {h.Right}</noparse>", new Vector2(0f, top - 2f), 0.42f, W * 0.68f, new Color(0.85f, 0.95f, 1f));
            barW = W * 0.6f;
            Box("Panel", new Vector2(0f, bottom + 2.2f), new Vector2(W * 0.7f, 3.6f), new Color(0.02f, 0.12f, 0.22f, 0.8f), 1);
            bar = Box("Bar", new Vector2(0f, bottom + 3.1f), new Vector2(barW, 0.35f), new Color(1f, 1f, 1f, 0.25f), 2);
            fill = Box("Fill", new Vector2(-barW / 2f, bottom + 3.1f), new Vector2(0.1f, 0.35f), new Color(1f, 0.85f, 0.35f, 1f), 3);
            time = Text("", new Vector2(-W * 0.24f, bottom + 1.9f), 0.5f, 12f, Color.white);
            info = Text("", new Vector2(W * 0.13f, bottom + 1.9f), 0.42f, W * 0.4f, new Color(0.85f, 0.95f, 1f));
        }

        private void Update()
        {
            var p = ReplayPlayer.Current;
            if (p == null || time == null) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool pause = false, slower = false, faster = false, back = false, fwd = false, cam = false, exit = false, restart = false;
            if (kb != null)
            {
                pause = kb.spaceKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame;
                slower = kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame || kb.leftBracketKey.wasPressedThisFrame;
                faster = kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame || kb.rightBracketKey.wasPressedThisFrame;
                back = kb.leftArrowKey.wasPressedThisFrame; fwd = kb.rightArrowKey.wasPressedThisFrame;
                cam = kb.cKey.wasPressedThisFrame; exit = kb.escapeKey.wasPressedThisFrame; restart = kb.homeKey.wasPressedThisFrame || kb.rKey.wasPressedThisFrame;
            }
            foreach (var g in UnityEngine.InputSystem.Gamepad.all)
            {
                pause |= g.buttonSouth.wasPressedThisFrame; slower |= g.leftShoulder.wasPressedThisFrame; faster |= g.rightShoulder.wasPressedThisFrame;
                back |= g.dpad.left.wasPressedThisFrame; fwd |= g.dpad.right.wasPressedThisFrame; cam |= g.buttonNorth.wasPressedThisFrame;
                exit |= g.buttonEast.wasPressedThisFrame; restart |= g.selectButton.wasPressedThisFrame;
            }
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Camera.main != null)
            {
                // a click on the time bar seeks there
                var w = Camera.main.ScreenToWorldPoint(mouse.position.ReadValue());
                var local = transform.InverseTransformPoint(w);
                var c = bar.transform.localPosition;
                if (Mathf.Abs(local.y - c.y) < 1f && Mathf.Abs(local.x - c.x) < barW / 2f) p.Seek((local.x - (c.x - barW / 2f)) / barW * p.Length);
            }
            if (exit) { Replays.Stop(); return; }
            if (pause) { if (p.AtEnd) p.Seek(0f); else p.Paused = !p.Paused; }
            if (restart) { p.Seek(0f); p.Paused = false; }
            int si = Mathf.Max(0, Array.IndexOf(Speeds, p.Speed));
            if (slower) p.Speed = Speeds[Mathf.Max(0, si - 1)];
            if (faster) p.Speed = Speeds[Mathf.Min(Speeds.Length - 1, si + 1)];
            if (back) p.Seek(p.Position - 5f);
            if (fwd) p.Seek(p.Position + 5f);
            if (cam) CameraMode = (CameraMode + 1) % Cams.Length;
            float k = p.Length > 0f ? p.Position / p.Length : 0f;
            fill.size = new Vector2(Mathf.Max(0.1f, barW * k), 0.35f);
            fill.transform.localPosition = new Vector3(-barW / 2f + barW * k / 2f, fill.transform.localPosition.y, fill.transform.localPosition.z);
            time.text = $"{(p.Paused ? "<color=#FFD95A>PAUSED</color>" : "PLAYING")}  {Clock(p.Position)} / {Clock(p.Length)}  ·  {p.Speed:0.##}X";
            bool pad = GamepadActive;
            info.text = pad ? $"A PLAY / PAUSE · LB / RB SPEED · D-PAD SEEK 5 S · Y CAMERA: {Cams[CameraMode]} · SELECT RESTART · B EXIT"
                            : $"SPACE PLAY / PAUSE · [ ] SPEED · ARROWS SEEK 5 S · C CAMERA: {Cams[CameraMode]} · R RESTART · ESC EXIT";
        }

        private static string Clock(float s) => $"{(int)(s / 60)}:{(int)(s % 60):00}";

        // the camera modes (after the game's camera has moved this frame)
        protected override void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null && CameraMode != 0)
            {
                var ball = FindFirstObjectByType<BallMovement>();
                // the floor (y -10) sits about a quarter up the screen, above the controls
                if (CameraMode == 1)
                {
                    float size = 118f / Mathf.Max(0.5f, cam.aspect) / 2f;
                    cam.orthographicSize = size;
                    cam.transform.position = new Vector3(0f, -10f + size * 0.5f, cam.transform.position.z);
                }
                else if (ball != null)
                {
                    float size = 24f;
                    cam.orthographicSize = size;
                    var b = ball.transform.position;
                    cam.transform.position = new Vector3(Mathf.Clamp(b.x, -80f, 80f), Mathf.Max(-10f + size * 0.6f, b.y - size * 0.3f), cam.transform.position.z);
                }
            }
            base.LateUpdate();
        }
    }
}

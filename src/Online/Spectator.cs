using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Patches;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HangtimeOvertime.Online
{
    // The camera choices of the replay viewer and spectators: the game's own, the whole court, or following the ball.
    internal static class CameraModes
    {
        public static int Mode;
        public static readonly string[] Names = { "GAME", "WHOLE COURT", "FOLLOW BALL" };
        public static void Next() => Mode = (Mode + 1) % Names.Length;

        // after the game's camera has moved this frame; the floor (y -10) sits about a quarter up the screen
        public static void Apply()
        {
            var cam = Camera.main;
            if (cam == null || Mode == 0) return;
            if (Mode == 1)
            {
                float size = 118f / Mathf.Max(0.5f, cam.aspect) / 2f;
                cam.orthographicSize = size;
                cam.transform.position = new Vector3(0f, -10f + size * 0.5f, cam.transform.position.z);
                return;
            }
            var ball = Object.FindFirstObjectByType<BallMovement>();
            if (ball == null) return;
            float s = 24f;
            cam.orthographicSize = s;
            var b = ball.transform.position;
            cam.transform.position = new Vector3(Mathf.Clamp(b.x, -80f, 80f), Mathf.Max(-10f + s * 0.6f, b.y - s * 0.3f), cam.transform.position.z);
        }
    }

    // A friend's PC watching (WATCH ONLY in the lobby, or joined while a match was on): the score and round wins, the
    // camera choices, both builds (from the last between-rounds screen), the party cup's bracket; chat and emotes go
    // to the log only (a spectator has no player to put a bubble on). Its devices never take a seat.
    internal class SpectatorHud : OvertimePanel
    {
        private static SpectatorHud current;
        public static RoundScreen LastScreen;            // the host's last between-rounds screen (the builds)
        public static BracketModel LastBracket;          // the party cup's last bracket
        private TextMeshPro top, hint, builds;
        private SpriteRenderer buildsBack;
        private bool showBuilds;

        // a friend's PC in a match with none of its devices seated
        public static bool Watching =>
            Net.IsClient && VersusState.Active && MatchSync.Mirroring && !Replays.Playing &&
            !VersusState.Setup.Slots.Any(s => s.Input != null && Net.MyDevice(s.Input) >= 0);

        public static void Tick()
        {
            if (current != null || !Watching || Camera.main == null) return;
            var go = new GameObject("Overtime Spectator");
            current = go.AddComponent<SpectatorHud>();
            if (!current.InitKit(230)) { Destroy(go); current = null; return; }
            current.Build();
            Plugin.Log.LogInfo("Spectator: watching round " + VersusState.Round);
        }

        private void Build()
        {
            float top0 = H / 2f;
            Box("Top", new Vector2(0f, top0 - 1.5f), new Vector2(W * 0.66f, 2.4f), new Color(0.02f, 0.12f, 0.22f, 0.78f), 1);
            top = Text("", new Vector2(0f, top0 - 1.1f), 0.55f, W * 0.64f, Color.white);
            hint = Text("", new Vector2(0f, top0 - 2.2f), 0.38f, W * 0.64f, new Color(0.85f, 0.95f, 1f));
            buildsBack = Box("Builds", new Vector2(0f, 2f), new Vector2(W * 0.6f, H * 0.5f), new Color(0.02f, 0.12f, 0.22f, 0.88f), 1);
            builds = Text("", new Vector2(0f, 2f), 0.64f, W * 0.56f, Color.white, TextAlignmentOptions.Top, height: H * 0.46f, wrap: true);
            buildsBack.enabled = false; builds.gameObject.SetActive(false);
        }

        public static bool DevDemo;                      // development (-ovt-watchdemo): steps through the views by itself
        private float shownAt = -1f;

        private void Update()
        {
            if (!Watching) { Destroy(gameObject); return; }
            if (shownAt < 0f) shownAt = Time.unscaledTime;
            float t = Time.unscaledTime - shownAt;
            if (DevDemo && t > 6f && CameraModes.Mode == 0 && !showBuilds && t < 7f) { CameraModes.Mode = 1; Plugin.Log.LogInfo("[watch] camera: whole court"); }
            if (DevDemo && t > 12f && t < 13f && CameraModes.Mode == 1) { CameraModes.Mode = 2; Plugin.Log.LogInfo("[watch] camera: follow ball"); }
            if (DevDemo && t > 18f && t < 19f && !showBuilds) { CameraModes.Mode = 0; showBuilds = true; ShowBuilds(); Plugin.Log.LogInfo("[watch] builds shown"); }
            var s = VersusState.Setup;
            var gm = GameManager.Instance;
            int[] w = VersusState.RoundWins;
            top.text = $"<color=#FFD95A>WATCHING</color>  ·  <noparse>{s.SideLabel(0)}</noparse>  <color=#FFD95A>{w[0]} - {w[1]}</color>  <noparse>{s.SideLabel(1)}</noparse>" +
                       $"  ·  ROUND {VersusState.Round}  ·  POINTS {gm?.playerPoints}-{gm?.opponentPoints}  ·  FIRST TO {VersusState.Target}";
            bool pad = GamepadActive;
            hint.text = pad ? $"Y: CAMERA ({CameraModes.Names[CameraModes.Mode]})  ·  X: BUILDS{(LastBracket != null ? "  ·  SELECT: BRACKET" : "")}  ·  D-PAD UP / DOWN: CHAT, EMOTES"
                            : $"C: CAMERA ({CameraModes.Names[CameraModes.Mode]})  ·  B: BUILDS{(LastBracket != null ? "  ·  TAB: BRACKET" : "")}  ·  Q / E / ENTER: CHAT, EMOTES";
            if (ChatHud.Busy) return;
            var kb = Keyboard.current;
            bool cam = kb != null && kb.cKey.wasPressedThisFrame, bl = kb != null && kb.bKey.wasPressedThisFrame, br = kb != null && kb.tabKey.wasPressedThisFrame;
            foreach (var g in Gamepad.all) { cam |= g.buttonNorth.wasPressedThisFrame; bl |= g.buttonWest.wasPressedThisFrame; br |= g.selectButton.wasPressedThisFrame; }
            if (cam) CameraModes.Next();
            if (bl) { showBuilds = !showBuilds; ShowBuilds(); }
            if (br && LastBracket != null && !BracketPanel.IsOpen) BracketPanel.Show(LastBracket, false);
        }

        private void ShowBuilds()
        {
            buildsBack.enabled = showBuilds; builds.gameObject.SetActive(showBuilds);
            if (!showBuilds) return;
            var d = LastScreen;
            string Side(int i) => d == null || d.Builds[i].Count == 0 ? "NO UPGRADES YET" : string.Join("\n", d.Builds[i].Select(b => b.ToUpperInvariant()));
            var st = VersusState.Setup;
            builds.text = $"<color=#FFD95A><noparse>{st.SideLabel(0)}</noparse></color>\n{Side(0)}\n\n<color=#FFD95A><noparse>{st.SideLabel(1)}</noparse></color>\n{Side(1)}" +
                          (d == null ? "" : "\n\n<size=75%><color=#BFE6FF>(AS OF THE LAST ROUND)</color></size>");
        }

        protected override void LateUpdate() { CameraModes.Apply(); base.LateUpdate(); }
        protected override void OnDestroy() { base.OnDestroy(); if (current == this) current = null; CameraModes.Mode = 0; }
    }
}

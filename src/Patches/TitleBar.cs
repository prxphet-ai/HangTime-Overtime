using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // The title screen's top-left bar: the mod's own buttons (SETTINGS, and later ones) and the update notice. It hides
    // while any of the mod's menus is open or the title menu isn't showing.
    internal class TitleBar : OvertimePanel
    {
        private static TitleBar current;
        internal static readonly List<(string label, Action open)> Buttons = new List<(string, Action)>();
        private readonly List<GameObject> parts = new List<GameObject>();
        private TextMeshPro notice;
        public static string Notice;                      // a line under the buttons (the update checker's)
        public static Action NoticeClick;

        public static void Init()
        {
            if (Buttons.Count > 0) return;
            Buttons.Add(("SETTINGS", () => SettingsMenu.Open()));
            Buttons.Add(("DAILY RUN", () => DailyMenu.Open()));
            Buttons.Add(("DRAFT", () => DraftMenu.Open()));
            Buttons.Add(("TOURNAMENTS", () => TournamentMenu.Open()));
            Buttons.Add(("COLLECTION", () => CollectionMenu.Open()));
            Buttons.Add(("REPLAYS", () => ReplayMenu.Open()));
            Buttons.Add(("LEADERBOARDS", () => LeaderboardMenu.Open()));
            Buttons.Add(("CONTROLS", () => ControlsMenu.Open()));
            Buttons.Add(("REPORT BUG", () => BugReport.Open()));
        }

        // NetRunner: one per title scene
        public static void Tick()
        {
            bool show = TitleController.onTitle && VersusMenu.TitleReady && !OverlayOpen();
            if (current == null)
            {
                if (!TitleController.onTitle || !VersusMenu.TitleReady || Camera.main == null) return;
                UpdateCheck.Start();
                var go = new GameObject("Overtime Title Bar");
                current = go.AddComponent<TitleBar>();
                if (!current.InitKit(150)) { Destroy(go); current = null; return; }
                current.Build();
            }
            current.SetVisible(show);
        }

        // any full-screen panel of the mod
        public static bool OverlayOpen() => VersusMenu.IsOpen || Online.LobbyBrowser.IsOpen || SettingsMenu.IsOpen || ControlsMenu.IsOpen || BugReport.IsOpen || CollectionMenu.IsOpen || DailyMenu.IsOpen || DraftMenu.IsOpen || ReplayMenu.IsOpen || LeaderboardMenu.IsOpen || TournamentMenu.IsOpen || TournamentEditor.IsOpen || CreatorMenu.IsOpen || ContinueMenu.IsOpen || PanelCount > 0;
        public static int PanelCount;                     // other menus register themselves while open

        private bool barFocus;                           // a gamepad is driving the bar (SELECT), the title menu is held
        private TMPro.TextMeshPro hint;

        private void Update()
        {
            if (!visible) return;
            if (!barFocus)
            {
                if (hint != null) hint.gameObject.SetActive(GamepadActive);
                HideFocusFrame();
                foreach (var g in UnityEngine.InputSystem.Gamepad.all)
                    if (g.selectButton.wasPressedThisFrame) { barFocus = true; HoldTitle(); focus = 0; return; }
                return;
            }
            if (hint != null) hint.gameObject.SetActive(false);
            NavUpdate();
        }

        // B, or opening one of the bar's menus, gives the title menu back
        protected override void OnNavBack() { barFocus = false; ReleaseTitle(); HideFocusFrame(); }

        private void Build()
        {
            float w = 5.5f, x = -W / 2f + 0.5f + w / 2f, y = H / 2f - 1.6f;
            foreach (var (label, open) in Buttons)
            {
                var b = Button(label, new Vector2(x, y), new Vector2(w, 1.8f), () => { if (barFocus) OnNavBack(); open(); }, label.Length > 10 ? 0.37f : 0.43f);
                parts.Add(b.gameObject);
                x += w + 0.2f;
            }
            hint = Text("SELECT: THIS BAR", new Vector2(x + 4f, y), 0.42f, 8f, new Color(0.7f, 1f, 0.75f), TextAlignmentOptions.Left);
            notice = Text("", new Vector2(-W / 2f + 1f + 11f, y - 2.1f), 0.5f, 22f, new Color(1f, 0.85f, 0.35f), TextAlignmentOptions.Left);
            var nb = notice.gameObject.AddComponent<BoxCollider2D>();
            nb.size = new Vector2(22f / notice.transform.lossyScale.x * 0.02f, 1.4f / notice.transform.lossyScale.y * 0.02f);
            var w2 = notice.gameObject.AddComponent<CreatorWidget>();
            w2.OnClick = () => NoticeClick?.Invoke();
        }

        private bool visible = true;
        private void SetVisible(bool v)
        {
            if (notice != null) notice.text = Notice ?? "";
            if (v == visible) return;
            visible = v;
            if (!v && barFocus) { barFocus = false; ReleaseTitle(); }
            foreach (Transform t in transform) t.gameObject.SetActive(v);
        }

        protected override void OnDestroy() { base.OnDestroy(); if (current == this) current = null; }
    }

    // The mod's settings (stored in its BepInEx config, never in the game's save).
    internal class SettingsMenu : OvertimePanel
    {
        private static SettingsMenu open;
        public static bool IsOpen => open != null;

        // each row: label, getter, setter (a toggle)
        internal static readonly List<(string label, string help, Func<bool> get, Action<bool> set)> Toggles = new List<(string, string, Func<bool>, Action<bool>)>();

        public static void Init()
        {
            if (Toggles.Count > 0) return;
            Toggles.Add(("SKIP TUTORIAL", "THE TUTORIAL NEVER STARTS (PAUSE MENU > TUTORIAL STILL PLAYS IT)",
                () => Plugin.SkipTutorial.Value, v => { Plugin.SkipTutorial.Value = v; if (v) TutorialManager.onTutorial = false; }));
            Toggles.Add(("CHECK FOR UPDATES", "ON LAUNCH, ASK GITHUB IF A NEWER OVERTIME IS OUT (NOTHING ABOUT YOU IS SENT)",
                () => Plugin.CheckForUpdates.Value, v => { Plugin.CheckForUpdates.Value = v; if (!v) { TitleBar.Notice = null; } else UpdateCheck.Start(); }));
            Toggles.Add(("RECORD REPLAYS", "KEEP A REPLAY OF EVERY MATCH AND VERSUS ROUND (THE LAST 10; REPLAYS > SAVE KEEPS ONE)",
                () => Plugin.RecordReplays.Value, v => Plugin.RecordReplays.Value = v));
            Toggles.Add(("SUBMIT TO LEADERBOARDS", "SEND YOUR CLASSIC SPEEDRUN AND INFINITE RESULTS TO THE GLOBAL BOARDS (NAME, RESULT AND RUN LOG)",
                () => Plugin.CommunitySubmit.Value, v => Plugin.CommunitySubmit.Value = v));
            Toggles.Add(("CHAT BUBBLES", "IN MATCHES, CHAT AND EMOTES SHOW ABOVE THE PLAYER (OFF: ONLY THE CHAT LOG IN THE CORNER)",
                () => Plugin.ChatBubbles.Value, v => Plugin.ChatBubbles.Value = v));
            Toggles.Add(("MUTE OTHERS' CHAT", "ONLINE: HIDE OTHER PLAYERS' CHAT LINES AND EMOTES",
                () => Plugin.ChatMuteOthers.Value, v => Plugin.ChatMuteOthers.Value = v));
        }

        public static void Open()
        {
            if (open != null) return;
            var go = new GameObject("Overtime Settings");
            open = go.AddComponent<SettingsMenu>();
            if (!open.InitKit(200)) { Destroy(go); open = null; return; }
            open.Build();
        }

        private void Build()
        {
            HoldTitle();
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            float pw = W * 0.6f, ph = H * 0.8f, top = ph / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            Text("OVERTIME SETTINGS", new Vector2(0f, top - 2f), 1.1f, pw - 2f, Color.white);
            float y = top - 5f;
            foreach (var (label, help, get, set) in Toggles)
            {
                Toggle(label, new Vector2(0f, y), new Vector2(pw * 0.6f, 2.2f), get, set, 0.6f);
                Text(help, new Vector2(0f, y - 1.8f), 0.4f, pw - 2f, new Color(0.85f, 0.95f, 1f));
                y -= 4.4f;
            }
            Button("BACK", new Vector2(0f, -ph / 2f + 1.8f), new Vector2(pw * 0.3f, 2.4f), Close, 0.7f);
            PadHint("A: CHANGE   ·   B: BACK", new Vector2(0f, -ph / 2f + 3.6f), pw - 2f);
            navKeys = true;
            focus = 0;
        }

        protected override void OnNavBack() => Close();

        private void Update()
        {
            if (FieldKeys() || NavUpdate()) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
        }

        private void Close() { Destroy(gameObject); }

        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }
    }

    internal static class TutorialSkip
    {
        // the game decides at startup (GameManager.Awake) whether the tutorial plays: the mod's setting says no
        [HarmonyPatch(typeof(GameManager), "Awake")]
        private static class Skip
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                // development (DevKeys + -ovt-forcetutorial): start as if the tutorial had never been finished
                if (Plugin.DevKeys.Value && Array.IndexOf(Environment.GetCommandLineArgs(), "-ovt-forcetutorial") >= 0) TutorialManager.onTutorial = true;
                if (Plugin.SkipTutorial.Value && TutorialManager.onTutorial) { TutorialManager.onTutorial = false; Plugin.Log.LogInfo("Tutorial skipped (setting)"); }
                Plugin.Log.LogInfo($"Tutorial at launch: {(TutorialManager.onTutorial ? "plays" : "off")} (skip setting {(Plugin.SkipTutorial.Value ? "on" : "off")})");
            }
        }
    }

    // A button group subscribes to the pad's A one frame after it's enabled, from a coroutine that keeps running when the
    // group is disabled again in between: a panel that releases the title menu and holds it again in the same frame (the
    // title bar opening CONTROLS) left the hidden menu pressing its buttons. A disabled group presses nothing.
    internal static class HeldGroupGuard
    {
        [HarmonyPatch(typeof(ButtonGroup), "Select")]
        private static class Guard
        {
            [HarmonyPrefix]
            private static bool Prefix(ButtonGroup __instance) => __instance.isActiveAndEnabled;
        }
    }
}

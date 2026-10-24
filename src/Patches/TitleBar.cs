using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // The title screen's top bar: the mod's menus as hand-drawn icon tiles in three groups (PLAY, COMMUNITY, GAME) on a
    // drawn strip, a line under it saying whether the community server answers, and the update notice. It hides while any
    // of the mod's menus is open or the title menu isn't showing. SELECT gives the bar to a gamepad.
    internal class TitleBar : OvertimePanel
    {
        private static TitleBar current;
        internal static readonly List<(string group, string icon, string label, Action open)> Buttons = new List<(string, string, string, Action)>();
        private readonly List<GameObject> parts = new List<GameObject>();
        private TextMeshPro notice;
        public static string Notice;                      // a line under the buttons (the update checker's)
        public static Action NoticeClick;

        public static void Init()
        {
            if (Buttons.Count > 0) return;
            Buttons.Add(("PLAY", "sun", "DAILY", () => DailyMenu.Open()));
            Buttons.Add(("PLAY", "cards", "DRAFT", () => DraftMenu.Open()));
            Buttons.Add(("PLAY", "trophy", "TOURNEYS", () => TournamentMenu.Open()));
            Buttons.Add(("COMMUNITY", "podium", "BOARDS", () => LeaderboardMenu.Open()));
            Buttons.Add(("COMMUNITY", "film", "REPLAYS", () => ReplayMenu.Open()));
            Buttons.Add(("COMMUNITY", "book", "COLLECTION", () => CollectionMenu.Open()));
            Buttons.Add(("GAME", "gear", "SETTINGS", () => SettingsMenu.Open()));
            Buttons.Add(("GAME", "pad", "CONTROLS", () => ControlsMenu.Open()));
            Buttons.Add(("GAME", "bug", "REPORT BUG", () => BugReport.Open()));
        }

        // NetRunner: one per title scene
        public static void Tick()
        {
            bool show = TitleController.onTitle && VersusMenu.TitleReady && !OverlayOpen();
            if (current == null)
            {
                if (!TitleController.onTitle || !VersusMenu.TitleReady || Camera.main == null) return;
                UpdateCheck.Start();
                if (Plugin.DevKeys.Value) Debugging.PostDump.Log();
                var go = new GameObject("Overtime Title Bar");
                current = go.AddComponent<TitleBar>();
                if (!current.InitKit(150)) { Destroy(go); current = null; return; }
                current.Build();
            }
            current.SetVisible(show);
        }

        // any full-screen panel of the mod
        public static bool OverlayOpen() => VersusMenu.IsOpen || Online.LobbyBrowser.IsOpen || SettingsMenu.IsOpen || ControlsMenu.IsOpen || BugReport.IsOpen || CollectionMenu.IsOpen || DailyMenu.IsOpen || DraftMenu.IsOpen || ReplayMenu.IsOpen || LeaderboardMenu.IsOpen || TournamentMenu.IsOpen || TournamentEditor.IsOpen || CreatorMenu.IsOpen || ContinueMenu.IsOpen || RulesPanel.IsOpen || BanPanel.IsOpen || PartyMenu.IsOpen || BracketPanel.IsOpen || TourPicker.IsOpen || PanelCount > 0;
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

        private TextMeshPro status;
        private SpriteRenderer statusIcon;

        // the layout of the overhaul's mockup (1600 x 900 px = 64 x 36 layout units, 25 px a unit)
        private void Build()
        {
            float top = H / 2f, left = -W / 2f;
            Box("Panel", new Vector2(0f, top - 2.24f), new Vector2(W - 0.8f, 3.84f), new Color(UiArt.Navy.r, UiArt.Navy.g, UiArt.Navy.b, 0.96f), 0, true);
            const float tileW = 5.52f, tileH = 2.48f, step = 5.84f, groupGap = 1.6f;
            float x = left + 1.36f, ty = top - 2.6f;
            string lastGroup = null;
            foreach (var (group, icon, label, open) in Buttons)
            {
                if (group != lastGroup)
                {
                    if (lastGroup != null) x += groupGap;
                    Text(group, new Vector2(x + 4f, top - 0.95f), 0.42f, 8f, UiArt.Gold, TextAlignmentOptions.Left);
                    lastGroup = group;
                }
                var act = open;
                var b = Button("", new Vector2(x + tileW / 2f, ty), new Vector2(tileW, tileH), () => { if (barFocus) OnNavBack(); act(); }, 0.5f);
                Icon(icon, new Vector2(x + 1.05f, ty), 1.5f, UiArt.Ink, 8);
                Text(label, new Vector2(x + 3.5f, ty), label.Length > 8 ? 0.42f : 0.5f, 3.3f, UiArt.Ink);
                parts.Add(b.gameObject);
                x += step;
            }
            hint = Text("SELECT: THIS BAR", new Vector2(top > 0 ? W / 2f - 3.4f : 0f, ty), 0.42f, 5.6f, new Color(0.7f, 1f, 0.75f));
            // the community server line
            Box("Status", new Vector2(left + 11.4f, top - 5.5f), new Vector2(21.4f, 1.5f), new Color(UiArt.Dark.r, UiArt.Dark.g, UiArt.Dark.b, 0.8f), 0);
            statusIcon = Icon("globe", new Vector2(left + 1.7f, top - 5.5f), 1.0f, new Color(0.49f, 0.9f, 0.47f), 2);
            status = Text("", new Vector2(left + 12.5f, top - 5.5f), 0.42f, 19.5f, UiArt.Ink, TextAlignmentOptions.Left, 3);
            CheckServer();
            notice = Text("", new Vector2(left + 12.5f, top - 7.1f), 0.48f, 22f, UiArt.Gold, TextAlignmentOptions.Left);
            var nb = notice.gameObject.AddComponent<BoxCollider2D>();
            nb.size = new Vector2(22f / notice.transform.lossyScale.x * 0.02f, 1.4f / notice.transform.lossyScale.y * 0.02f);
            var w2 = notice.gameObject.AddComponent<CreatorWidget>();
            w2.OnClick = () => NoticeClick?.Invoke();
        }

        // asks the community server once per title screen whether it's there
        private void CheckServer()
        {
            void Show(string text, Color c) { if (status != null) status.text = text; if (statusIcon != null) statusIcon.color = UiArt.Paint(c); }
            if (!Online.Community.Configured) { Show("OFFLINE  ·  COMMUNITY FEATURES ARE OFF (SETTINGS FILE)", new Color(0.7f, 0.75f, 0.8f)); return; }
            Show("CONNECTING TO THE COMMUNITY SERVER...", UiArt.Gold);
            Online.Community.Get("/v1/health", (text, err) =>
            {
                if (this == null) return;
                if (err == null) Show("ONLINE  ·  LEADERBOARDS AND TOURNAMENT BROWSER CONNECTED", new Color(0.49f, 0.9f, 0.47f));
                else Show("OFFLINE  ·  RESULTS WAIT AND SEND WHEN THE SERVER CAN BE REACHED", UiArt.Orange);
            });
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

        private static readonly string[] Icons = { "play", "refresh", "film", "podium", "people", "back" };

        // six big cards in two columns: the setting, what it does, ON / OFF; the whole card switches it
        private void Build()
        {
            int keep = focus;
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops();
            HoldTitle();
            Frame("SETTINGS", Close);
            for (int i = 0; i < Toggles.Count; i++)
            {
                var (label, help, get, set) = Toggles[i];
                float x0 = i % 2 == 0 ? 80f : 820f, y0 = 172f + (i / 2) * 212f, x1 = x0 + 700f, y1 = y0 + 192f;
                bool on = get();
                var g = get; var st = set;
                Shade(Btn("", x0, y0, x1, y1, () => { st(!g()); Build(); }), on ? new Color(0.094f, 0.275f, 0.46f, 1f) : new Color(0.07f, 0.17f, 0.29f, 1f));
                Icon(i < Icons.Length ? Icons[i] : "gear", P(x0 + 50f, y0 + 50f), 1.7f, on ? UiArt.Gold : UiArt.Ink, 8);
                TL(label, x0 + 96f, y0 + 50f, 34, UiArt.Ink, 430);
                var pill = Rect("Pill", x1 - 150f, y0 + 28f, x1 - 26f, y0 + 72f, on ? new Color(0.36f, 0.8f, 0.4f, 1f) : new Color(0.85f, 0.32f, 0.26f, 1f), 7);
                TC(on ? "ON" : "OFF", x1 - 88f, y0 + 51f, 30, UiArt.Dark, 110);
                TW(help, (x0 + x1) / 2f, y0 + 130f, 21, new Color(0.78f, 0.88f, 1f), 640f, 90f);
            }
            PadHint("A: SWITCH   B: BACK", P(1180, 870), 380 / 25f * KX);
            navKeys = true;
            focus = keep >= 0 && keep < stops.Count ? keep : 0;
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

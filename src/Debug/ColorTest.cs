using System;
using HangtimeOvertime.Patches;
using UnityEngine;

namespace HangtimeOvertime.Debugging
{
    // development (DevKeys + -ovt-colortest): swatches of known colours, painted the way the menus paint their boxes, on an
    // opaque black card on the title screen; a screenshot shows how the game's rendering maps menu colours
    internal class ColorTest : OvertimePanel
    {
        private static bool shown;
        public static readonly float[] Levels = { 0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f };

        public static void Tick()
        {
            if (shown || !Plugin.DevKeys.Value || Array.IndexOf(Environment.GetCommandLineArgs(), "-ovt-colortest") < 0) return;
            if (!TitleController.onTitle || Camera.main == null) return;
            shown = true;
            var go = new GameObject("Overtime Color Test");
            var t = go.AddComponent<ColorTest>();
            if (!t.InitKit(900)) { Destroy(go); return; }
            t.Build();
        }

        private void Build()
        {
            Box("Card", Vector2.zero, new Vector2(W * 0.9f, 12f), Color.black, 0);
            float x0 = -W * 0.4f, step = W * 0.8f / (Levels.Length - 1);
            for (int i = 0; i < Levels.Length; i++)
            {
                float v = Levels[i];
                Box("Grey", new Vector2(x0 + i * step, 3.5f), new Vector2(step * 0.8f, 3f), new Color(v, v, v, 1f), 2);
                Box("Red", new Vector2(x0 + i * step, 0f), new Vector2(step * 0.8f, 3f), new Color(v, 0f, 0f, 1f), 2);
                Box("Blue", new Vector2(x0 + i * step, -3.5f), new Vector2(step * 0.8f, 3f), new Color(0f, 0f, v, 1f), 2);
            }
            Plugin.Log.LogInfo("Color test shown");
        }
    }
}

namespace HangtimeOvertime.Debugging
{
    // development (DevKeys + -ovt-uidebug): every few seconds, the title buttons' click areas and what's under the mouse
    internal static class UiDebug
    {
        private static float next;
        public static void Tick()
        {
            if (!Plugin.DevKeys.Value || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ovt-uidebug") < 0 || UnityEngine.Time.unscaledTime < next) return;
            next = UnityEngine.Time.unscaledTime + 3f;
            var cam = UnityEngine.Camera.main;
            if (cam == null) return;
            var m = UnityEngine.InputSystem.Mouse.current;
            var wp = m != null ? (UnityEngine.Vector2)cam.ScreenToWorldPoint(m.position.ReadValue()) : UnityEngine.Vector2.zero;
            var hits = UnityEngine.Physics2D.OverlapPointAll(wp, ~0);
            Plugin.Log.LogInfo($"[uidebug] mouse {wp} hits: {string.Join(", ", System.Linq.Enumerable.Select(hits, h => $"{h.name}(layer {h.gameObject.layer}, enabled {h.enabled})"))}");
            foreach (var b in UnityEngine.Object.FindObjectsByType<ButtonController>(UnityEngine.FindObjectsSortMode.None))
            {
                var c = b.GetComponent<UnityEngine.Collider2D>();
                Plugin.Log.LogInfo($"[uidebug] {b.name}: layer {b.gameObject.layer} collider {(c != null ? c.enabled.ToString() : "none")} interactable {b.interactable} wasClicked {b.wasClicked} hovering {b.isHovering}");
            }
            Plugin.Log.LogInfo($"[uidebug] cameras: {string.Join(", ", System.Linq.Enumerable.Select(UnityEngine.Camera.allCameras, k => $"{k.name} depth {k.depth} mask {k.cullingMask} eventMask {k.eventMask}"))}");
        }
    }
}

namespace HangtimeOvertime.Debugging
{
    // development (DevKeys + -ovt-open NAME): opens one of the menus on the title screen a few seconds after it loads, for
    // screenshots: creator, versus, coop, continue, lobby, browser, or a title-bar menu by its label (draft, daily, ...)
    internal static class DevOpen
    {
        private static bool done;
        private static float at = -1f;
        public static void Tick()
        {
            if (done || !Plugin.DevKeys.Value || !TitleController.onTitle || !Patches.VersusMenu.TitleReady) return;
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-ovt-open");
            if (i < 0 || i + 1 >= args.Length) { done = true; return; }
            if (at < 0f) at = UnityEngine.Time.unscaledTime + 3f;
            if (UnityEngine.Time.unscaledTime < at) return;
            done = true;
            string what = args[i + 1].ToLowerInvariant();
            UnityEngine.GameObject Btn(string name) => System.Linq.Enumerable.FirstOrDefault(UnityEngine.Object.FindObjectsByType<ButtonController>(UnityEngine.FindObjectsSortMode.None), b => b.name == name)?.gameObject;
            TitleButton Title(int players) => System.Linq.Enumerable.FirstOrDefault(UnityEngine.Object.FindObjectsByType<TitleButton>(UnityEngine.FindObjectsSortMode.None), b => Patches.ContinueMenu.Players(b) == players && !Patches.ContinueMenu.IsInfinite(b));
            switch (what)
            {
                case "creator": Btn("Customize Button")?.GetComponent<Patches.PopupClick>()?.Click(); break;
                case "versus": Btn("Versus Button")?.GetComponent<Patches.PopupClick>()?.Click(); break;
                case "coop": Patches.ContinueMenu.ShowCoop(Title(2)); break;
                case "continue": Patches.ContinueMenu.Show(Title(1), false); break;
                case "lobby": Patches.VersusMenu.OpenLobby(); break;
                case "browser": Online.LobbyBrowser.Open(); break;
                case "party": Patches.PartyMenu.Open(); break;
                case "rules": Patches.RulesPanel.Open(); break;
                case "editor": Patches.TournamentEditor.Open(System.Linq.Enumerable.FirstOrDefault(Core.Tournaments.All)); break;
                case "bracket": Patches.BracketPanel.Show(Core.Bracket.Classic(3), false); break;
                default:
                    foreach (var (_, _, label, open) in Patches.TitleBar.Buttons)
                        if (label.ToLowerInvariant().StartsWith(what)) { open(); break; }
                    break;
            }
            Plugin.Log.LogInfo("Dev open: " + what);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HangtimeOvertime.Patches
{
    // CONTROLS (title bar): every action of the four keyboard key sets, the four gamepad slots and typed chat. Click a
    // binding, press the new key / button (Esc cancels); a key already used elsewhere is accepted with a warning and both
    // rows turn red. RESET TAB / RESET ALL go back to the defaults. Saved at once (Controls).
    internal class ControlsMenu : OvertimePanel
    {
        private static ControlsMenu open;
        public static bool IsOpen => open != null;
        private static readonly string[] Tabs = { "KEYS 1", "KEYS 2", "KEYS 3", "KEYS 4", "PAD 1", "PAD 2", "PAD 3", "PAD 4", "CHAT" };
        private static int tab;
        private Action<Key> captureKey;                // waiting for a key (or null)
        private Action<string> capturePad;             // waiting for a gamepad button (or null)
        private float captureFrom;
        private string warning = "";
        private TextMeshPro status;

        public static void Open()
        {
            if (open != null) return;
            var go = new GameObject("Overtime Controls");
            open = go.AddComponent<ControlsMenu>();
            if (!open.InitKit(200)) { Destroy(go); open = null; return; }
            if (TitleController.onTitle) open.HoldTitle();
            open.Build();
        }

        private void Build()
        {
            int keep = focus;
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops();
            navKeys = true;
            Frame("CONTROLS", () => Destroy(gameObject));
            float tw = 1480f / Tabs.Length - 8f;
            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                float x0 = 60f + i * (tw + 8f);
                Shade(Btn("", x0, i == tab ? 92 : 98, x0 + tw, 150, () => { tab = idx; captureKey = null; capturePad = null; warning = ""; Build(); }), i == tab ? UiArt.Navy : UiArt.Blue);
                TC(Tabs[i], x0 + tw / 2f, 121, 22, i == tab ? UiArt.Gold : UiArt.Ink, tw - 10);
            }
            TW(Help(), 800, 186, 20, new Color(0.78f, 0.88f, 1f), 1400, 50);
            float y = 226f;
            foreach (var (label, value, used, start) in Rows())
            {
                Rect("Row", 260, y, 1340, y + 60, new Color(0.094f, 0.275f, 0.46f, 1f), 3);
                TR(label, 700, y + 30, 28, UiArt.Ink, 420);
                var b = Btn("", 740, y + 7, 1320, y + 53, start, 26);
                Shade(b, new Color(0.03f, 0.11f, 0.2f, 1f));
                TC(used != null ? $"<color=#FF8A7C>{value}</color>  <size=70%><color=#FFD24A>ALSO {used}</color></size>" : value, 1030, y + 30, 28, UiArt.Gold, 560);
                y += 68f;
            }
            IconBtn("refresh", "RESET TAB", 70, 846, 330, 892, ResetTab, 24);
            IconBtn("refresh", "RESET ALL", 350, 846, 610, 892, () => { Controls.ResetAll(); warning = "EVERYTHING IS BACK TO THE DEFAULTS"; Build(); }, 24);
            PadHint("A: REBIND   B: BACK   LB / RB: TABS", P(1130, 869), 400 / 25f * KX);
            focus = keep >= 0 && keep < stops.Count ? keep : Tabs.Length;   // first row
            status = TC(captureKey != null ? "PRESS THE NEW KEY  (ESC / B / START CANCELS)" : capturePad != null ? "PRESS THE NEW BUTTON ON ANY GAMEPAD  (ESC / START CANCELS)" : warning,
                        800, 806, 26, UiArt.Gold, 1400);
        }

        private string Help()
        {
            if (tab < 4) return $"KEY SET {tab + 1}: VERSUS / ONLINE: A PLAYER JOINS WITH ANY KEY OF THE SET.  CLASSIC & INFINITE: SETS 1 AND 2 BOTH CONTROL YOU.  CO-OP: SET 2 = PLAYER 1, SET 1 = PLAYER 2.";
            if (tab < 8) return $"GAMEPAD SLOT {tab - 3}: THE {Ordinal(tab - 3)} CONNECTED GAMEPAD. MOVE WITH THE LEFT STICK OR D-PAD LEFT / RIGHT. MENU OK AND BACK ONLY COUNT IN MENUS.";
            return "TYPED CHAT IN MATCHES (KEYBOARD). GAMEPAD PLAYERS OPEN IT WITH THEIR PAD'S TYPED CHAT BUTTON (PAD TABS).";
        }

        private static string Ordinal(int n) => n == 1 ? "FIRST" : n == 2 ? "SECOND" : n == 3 ? "THIRD" : "FOURTH";

        // (label, current binding, "also used by" or null, start rebinding)
        private IEnumerable<(string, string, string, Action)> Rows()
        {
            if (tab < 4)
            {
                int set = tab;
                for (int a = 0; a < Controls.KeyActs; a++)
                {
                    int act = a;
                    var k = Controls.KeyOf(set, (KeyAct)a);
                    yield return (Controls.KeyActNames[a], Controls.KeyName(k), Controls.KeyUsedBy(k, set, act, false),
                        () => Capture(key => { Controls.SetKey(set, (KeyAct)act, key); Warn(Controls.KeyUsedBy(key, set, act, false), Controls.KeyName(key)); }));
                }
            }
            else if (tab < 8)
            {
                int slot = tab - 4;
                for (int a = 0; a < Controls.PadActs; a++)
                {
                    int act = a;
                    var c = Controls.PadOf(slot, (PadAct)a);
                    yield return (Controls.PadActNames[a], Controls.PadName(c), Controls.PadUsedBy(slot, c, act),
                        () => CapturePad(ctl => { Controls.SetPad(slot, (PadAct)act, ctl); Warn(Controls.PadUsedBy(slot, ctl, act), Controls.PadName(ctl)); }));
                }
            }
            else
            {
                var k = Controls.ChatKey;
                yield return ("TYPED CHAT", Controls.KeyName(k), Controls.KeyUsedBy(k, -1, -1, true) is string u && !u.Contains("MENU OK") ? u : null,
                    () => Capture(key => { Controls.SetChatKey(key); var u2 = Controls.KeyUsedBy(key, -1, -1, true); Warn(u2 != null && !u2.Contains("MENU OK") ? u2 : null, Controls.KeyName(key)); }));
            }
        }

        private void Warn(string usedBy, string name) => warning = usedBy != null ? $"{name} IS ALSO USED FOR {usedBy}: BOTH WILL TRIGGER" : $"SET TO {name}";

        private void Capture(Action<Key> done) { captureKey = done; capturePad = null; captureFrom = Time.unscaledTime; Build(); }
        private void CapturePad(Action<string> done) { capturePad = done; captureKey = null; captureFrom = Time.unscaledTime; Build(); }

        private void ResetTab()
        {
            if (tab < 4) Controls.ResetSet(tab);
            else if (tab < 8) Controls.ResetSlot(tab - 4);
            else Controls.SetChatKey(Key.Enter);
            warning = $"{Tabs[tab]} IS BACK TO ITS DEFAULTS";
            Build();
        }

        public static bool Capturing => open != null && (open.captureKey != null || open.capturePad != null);

        private void Update()
        {
            var kb = Keyboard.current;
            if (captureKey != null || capturePad != null)
            {
                if (Time.unscaledTime - captureFrom < 0.15f) return;          // the click that started it
                // START cancels; so does B while waiting for a key (a pad button can't be a key binding)
                bool padCancel = Gamepad.all.Any(g => g.startButton.wasPressedThisFrame || (captureKey != null && g.buttonEast.wasPressedThisFrame));
                if ((kb != null && kb.escapeKey.wasPressedThisFrame) || padCancel) { captureKey = null; capturePad = null; warning = "CANCELLED"; Build(); return; }
                if (captureKey != null && kb != null)
                {
                    var key = kb.allKeys.FirstOrDefault(k => k.wasPressedThisFrame && k.keyCode != Key.Escape);
                    if (key != null) { var done = captureKey; captureKey = null; done(key.keyCode); Build(); }
                    return;
                }
                if (capturePad != null)
                    foreach (var g in Gamepad.all)
                        foreach (var name in Controls.PadControls)
                        {
                            var c = g.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>(name);
                            if (c != null && c.wasPressedThisFrame) { var done = capturePad; capturePad = null; done(name); Build(); return; }
                        }
                return;
            }
            if (NavUpdate()) return;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Destroy(gameObject);
        }

        protected override void OnNavBack() => Destroy(gameObject);
        protected override void OnNavTab(int dir) { tab = (tab + dir + Tabs.Length) % Tabs.Length; warning = ""; focus = -1; Build(); }

        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }
    }
}

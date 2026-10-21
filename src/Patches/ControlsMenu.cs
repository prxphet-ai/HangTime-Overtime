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
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            float pw = W * 0.8f, ph = H * 0.88f, top = ph / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            Text("CONTROLS", new Vector2(0f, top - 1.8f), 1.1f, pw - 2f, Color.white);
            float tw = (pw - 2f) / Tabs.Length;
            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                var b = Button(Tabs[i], new Vector2(-pw / 2f + 1f + tw * (i + 0.5f), top - 4f), new Vector2(tw - 0.3f, 1.9f), () => { tab = idx; captureKey = null; capturePad = null; warning = ""; Build(); }, 0.48f);
                b.color = i == tab ? new Color(0.96f, 0.55f, 0.12f, 1f) : new Color(0.16f, 0.6f, 0.86f, 1f);
            }
            Text(Help(), new Vector2(0f, top - 6.2f), 0.42f, pw - 3f, new Color(0.85f, 0.95f, 1f), height: 2f, wrap: true);
            float y = top - 8.6f, rowH = 2.1f;
            foreach (var (label, value, used, start) in Rows())
            {
                Text(label, new Vector2(-pw * 0.24f, y), 0.55f, pw * 0.36f, Color.white, TextAlignmentOptions.Right);
                var b = Button("", new Vector2(pw * 0.14f, y), new Vector2(pw * 0.36f, 1.95f), start, 0.55f);
                Text(used != null ? $"<color=#FF8A7C>{value}</color>  <size=70%><color=#FFD95A>ALSO {used}</color></size>" : value,
                     new Vector2(pw * 0.14f, y), 0.55f, pw * 0.36f, Color.white, o: 9);
                y -= rowH;
            }
            float by = -ph / 2f + 1.7f;
            Button("RESET TAB", new Vector2(-pw * 0.27f, by), new Vector2(pw * 0.2f, 2.3f), ResetTab, 0.6f);
            Button("RESET ALL", new Vector2(0f, by), new Vector2(pw * 0.2f, 2.3f), () => { Controls.ResetAll(); warning = "EVERYTHING IS BACK TO THE DEFAULTS"; Build(); }, 0.6f);
            Button("BACK", new Vector2(pw * 0.27f, by), new Vector2(pw * 0.2f, 2.3f), () => Destroy(gameObject), 0.6f);
            PadHint("A: REBIND   ·   B: BACK   ·   LB / RB: TABS", new Vector2(0f, top - 7.3f), pw - 2f);
            focus = keep >= 0 && keep < stops.Count ? keep : Tabs.Length;   // first row
            status = Text(captureKey != null ? "PRESS THE NEW KEY  (ESC / B / START CANCELS)" : capturePad != null ? "PRESS THE NEW BUTTON ON ANY GAMEPAD  (ESC / START CANCELS)" : warning,
                          new Vector2(0f, by + 1.85f), 0.5f, pw - 2f, new Color(1f, 0.85f, 0.35f));
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

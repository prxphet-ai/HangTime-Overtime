using System;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HangtimeOvertime.Patches
{
    // Gamepad (and arrow-key) navigation for the mod's panels. Every Button / Toggle / TextField a panel makes is a stop;
    // the D-pad or left stick moves a highlight frame to the nearest stop in that direction, A activates (a text field
    // opens the on-screen keyboard), B goes back, LB / RB switch tabs. Prompts made with PadHint() only show while a
    // gamepad is the last thing used.
    internal abstract partial class OvertimePanel
    {
        internal class Stop
        {
            public SpriteRenderer Box;
            public Action Activate;
            public Field Field;
        }

        protected readonly List<Stop> stops = new List<Stop>();
        protected int focus = -1;
        protected bool navKeys;                          // arrow keys + Enter navigate too (panels whose keys aren't players')
        private SpriteRenderer frame;
        private float repeatAt;
        private Vector2 heldDir;
        private readonly List<TextMeshPro> padHints = new List<TextMeshPro>();

        // ------------------------------------------------------------ which device is in use (for prompts)

        private static float lastPad, lastKeys;
        private static int trackedFrame = -1;
        public static bool GamepadActive { get { Track(); return lastPad > lastKeys; } }

        private static void Track()
        {
            if (trackedFrame == Time.frameCount) return;
            trackedFrame = Time.frameCount;
            foreach (var g in Gamepad.all)
                if (g.wasUpdatedThisFrame && (g.leftStick.ReadValue().sqrMagnitude > 0.25f || g.allControls.Any(c => c is UnityEngine.InputSystem.Controls.ButtonControl b && b.wasPressedThisFrame)))
                    lastPad = Time.unscaledTime;
            var kb = Keyboard.current; var mouse = Mouse.current;
            if ((kb != null && kb.anyKey.wasPressedThisFrame) || (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.delta.ReadValue().sqrMagnitude > 4f)))
                lastKeys = Time.unscaledTime;
        }

        // ------------------------------------------------------------ stops

        protected Stop AddStop(SpriteRenderer box, Action act, Field field = null)
        {
            var s = new Stop { Box = box, Activate = act, Field = field };
            stops.Add(s);
            return s;
        }

        protected void ClearStops() { stops.Clear(); focus = -1; if (frame != null) frame.enabled = false; }

        // a prompt line that only shows while a gamepad is in use
        protected TextMeshPro PadHint(string text, Vector2 at, float width)
        {
            var t = Text(text, at, 0.45f, width, new Color(0.7f, 1f, 0.75f));
            padHints.Add(t);
            t.gameObject.SetActive(GamepadActive);
            return t;
        }

        protected virtual void OnNavBack() { }
        protected virtual void OnNavTab(int dir) { }

        // ------------------------------------------------------------ the frame loop (panels call it from Update)

        // true when it used the input this frame (the panel shouldn't read the same press again)
        protected bool NavUpdate()
        {
            bool pad = GamepadActive;
            foreach (var h in padHints) if (h != null && h.gameObject.activeSelf != pad) h.gameObject.SetActive(pad);
            stops.RemoveAll(s => s.Box == null);
            if (Typing || OnScreenKeyboard.IsOpen && !(this is OnScreenKeyboard)) return false;   // the pad keyboard has the pad

            Vector2 dir = Vector2.zero;
            bool ok = false, back = false, padOk = false;
            int tabDir = 0;
            foreach (var g in Gamepad.all)
            {
                var d = g.dpad.ReadValue();
                var st = g.leftStick.ReadValue();
                if (d.sqrMagnitude > 0.25f) dir = d;
                else if (st.sqrMagnitude > 0.35f) dir = st;
                int slot = Controls.SlotOf(g);
                var okB = Controls.Button(g, slot, PadAct.Confirm); var backB = Controls.Button(g, slot, PadAct.Back);
                padOk |= (okB != null && okB.wasPressedThisFrame);
                back |= (backB != null && backB.wasPressedThisFrame);
                if (g.leftShoulder.wasPressedThisFrame) tabDir = -1;
                if (g.rightShoulder.wasPressedThisFrame) tabDir = 1;
            }
            ok = padOk;
            var kb = Keyboard.current;
            if (navKeys && kb != null)
            {
                if (kb.upArrowKey.isPressed) dir = Vector2.up; else if (kb.downArrowKey.isPressed) dir = Vector2.down;
                else if (kb.leftArrowKey.isPressed) dir = Vector2.left; else if (kb.rightArrowKey.isPressed) dir = Vector2.right;
                ok |= focus >= 0 && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
            }
            if (tabDir != 0) { OnNavTab(tabDir); return true; }
            if (back) { OnNavBack(); return true; }

            // a direction: once, then repeating while held
            Vector2 step = Vector2.zero;
            if (dir.sqrMagnitude > 0.1f)
            {
                var snapped = Mathf.Abs(dir.x) > Mathf.Abs(dir.y) ? new Vector2(Mathf.Sign(dir.x), 0f) : new Vector2(0f, Mathf.Sign(dir.y));
                if (snapped != heldDir) { heldDir = snapped; step = snapped; repeatAt = Time.unscaledTime + 0.35f; }
                else if (Time.unscaledTime > repeatAt) { step = snapped; repeatAt = Time.unscaledTime + 0.12f; }
            }
            else heldDir = Vector2.zero;

            bool used = false;
            if (step != Vector2.zero && stops.Count > 0)
            {
                if (focus < 0 || focus >= stops.Count) focus = 0;
                else Move(step);
                used = true;
            }
            if (ok && stops.Count > 0)
            {
                if (focus < 0 || focus >= stops.Count) focus = 0;
                else
                {
                    var s = stops[focus];
                    if (s.Field != null && padOk) OnScreenKeyboard.Open(s.Field, this);   // Enter on a keyboard just types there
                    else s.Activate?.Invoke();
                }
                used = true;
            }
            ShowFrame(pad || navKeys && focus >= 0);
            return used;
        }

        private void Move(Vector2 d)
        {
            // first choice: the nearest stop in line with this one (their boxes overlap across the move); then the nearest
            // inside a 45° cone; up / down finally take anything that way (a tab row above wider rows), left / right stay
            // put at the end of a row
            var cur = stops[focus].Box;
            var from = Center(stops[focus]);
            int[] best = { -1, -1, -1 }; float[] bestScore = { float.MaxValue, float.MaxValue, float.MaxValue };
            for (int i = 0; i < stops.Count; i++)
            {
                if (i == focus || !stops[i].Box.gameObject.activeInHierarchy) continue;
                var v = Center(stops[i]) - from;
                float along = Vector2.Dot(v, d);
                if (along < 0.2f) continue;
                float side = Mathf.Abs(d.x != 0f ? v.y : v.x);
                float reach = d.x != 0f ? (cur.bounds.extents.y + stops[i].Box.bounds.extents.y) : (cur.bounds.extents.x + stops[i].Box.bounds.extents.x);
                int tier = side < reach * 0.8f ? 0 : side <= along ? 1 : 2;
                float score = along + side * 2.5f;
                if (score < bestScore[tier]) { bestScore[tier] = score; best[tier] = i; }
            }
            int pick = best[0] >= 0 ? best[0] : best[1] >= 0 ? best[1] : d.y != 0f ? best[2] : -1;
            if (pick >= 0) focus = pick;
        }

        private static Vector2 Center(Stop s) => s.Box.transform.position;

        private void ShowFrame(bool show)
        {
            if (focus < 0 || focus >= stops.Count || !show || !stops[focus].Box.gameObject.activeInHierarchy) { if (frame != null) frame.enabled = false; return; }
            var box = stops[focus].Box;
            if (frame == null)
            {
                frame = Box("Focus", Vector2.zero, Vector2.one, new Color(1f, 0.85f, 0.25f, 1f), 0);
                frame.name = "Focus frame";
            }
            frame.enabled = true;
            frame.transform.position = box.transform.position + new Vector3(0f, 0f, 0.001f);
            frame.size = box.size + new Vector2(0.5f, 0.45f);
            frame.sortingOrder = box.sortingOrder - 1;
        }

        protected void HideFocusFrame() { if (frame != null) frame.enabled = false; }

        // the stop for a box (to put the highlight somewhere on purpose)
        protected void FocusOn(SpriteRenderer box) { int i = stops.FindIndex(s => s.Box == box); if (i >= 0) focus = i; }
    }

    // A pad-friendly keyboard for text fields: letters, digits and a few symbols on a grid; A types, X deletes, START /
    // OK finishes, B / CANCEL closes without changing the field.
    internal class OnScreenKeyboard : OvertimePanel
    {
        private static OnScreenKeyboard open;
        public static bool IsOpen => open != null;
        public static OvertimePanel Owner => open?.owner;
        private Field target;
        private OvertimePanel owner;
        private string value;
        private TextMeshPro preview;
        private static readonly string[] Rows = { "1234567890", "QWERTYUIOP", "ASDFGHJKL-", "ZXCVBNM.,'", "!?:()/_+#&" };

        public static void Open(Field f, OvertimePanel from)
        {
            if (open != null || f == null) return;
            var go = new GameObject("Overtime On-screen Keyboard");
            open = go.AddComponent<OnScreenKeyboard>();
            open.target = f; open.owner = from; open.value = f.Value ?? "";
            if (!open.InitKit(480)) { Destroy(go); open = null; return; }
            open.Build();
        }

        private void Build()
        {
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.05f, 0.12f, 0.55f), 0);
            float pw = W * 0.62f, ph = H * 0.62f, top = ph / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.98f), 1);
            Box("Preview box", new Vector2(0f, top - 2.4f), new Vector2(pw - 2f, 2.4f), new Color(0.02f, 0.2f, 0.34f, 1f), 2);
            preview = Text("", new Vector2(0f, top - 2.4f), 0.65f, pw - 3f, Color.white);
            float kw = (pw - 2f) / 10f, y = top - 5.4f;
            for (int r = 0; r < Rows.Length; r++, y -= 2.15f)
                for (int c = 0; c < Rows[r].Length; c++)
                {
                    char ch = Rows[r][c];
                    var b = Button(ch.ToString(), new Vector2(-pw / 2f + 1f + kw * (c + 0.5f), y), new Vector2(kw - 0.25f, 1.9f), () => Type(ch.ToString()), 0.6f);
                    if (r == 1 && c == 0) FocusOn(b);
                }
            y -= 0.3f;
            Button("SPACE", new Vector2(-pw * 0.27f, y), new Vector2(pw * 0.26f, 1.9f), () => Type(" "), 0.55f);
            Button("DELETE", new Vector2(0f, y), new Vector2(pw * 0.2f, 1.9f), Delete, 0.55f);
            Button("OK", new Vector2(pw * 0.27f, y), new Vector2(pw * 0.2f, 1.9f), Done, 0.55f);
            PadHint("A: TYPE   ·   X: DELETE   ·   START: OK   ·   B: CANCEL", new Vector2(0f, -ph / 2f + 0.9f), pw - 2f);
            Show();
        }

        private void Type(string s) { if (value.Length < target.Max) { value += target.Upper ? s.ToUpperInvariant() : s.ToLowerInvariant() == s ? s : s; Show(); } }
        private void Delete() { if (value.Length > 0) { value = value.Substring(0, value.Length - 1); Show(); } }
        private void Show() => preview.text = value.Replace("<", "‹") + "<color=#FFD95A>|</color>";

        private void Done()
        {
            SetField(target, value);
            var cb = target.OnSubmit;
            Destroy(gameObject);
            cb?.Invoke(value);
        }

        protected override void OnNavBack() => Destroy(gameObject);

        private void Update()
        {
            foreach (var g in Gamepad.all)
            {
                if (g.buttonWest.wasPressedThisFrame) { Delete(); return; }
                if (g.startButton.wasPressedThisFrame) { Done(); return; }
            }
            NavUpdate();
        }

        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }
    }
}

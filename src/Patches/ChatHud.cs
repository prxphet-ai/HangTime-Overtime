using System;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Online;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using ChatDef = HangtimeOvertime.Generated.Chat;

namespace HangtimeOvertime.Patches
{
    // In-match chat: a quick chat wheel and an emote wheel per player (their key set's QUICK CHAT / EMOTE keys, or their
    // gamepad's), typed chat (the CHAT key, Enter by default; a gamepad's TYPED CHAT button opens the on-screen keyboard),
    // a bubble above the player's character that fades, and a chat log in the bottom-left corner. While a wheel or the
    // chat box is open, that player's character takes no gameplay input. Online: the host relays every line to all PCs
    // (characters by MatchSync's player index, the same on every PC). Settings: chat bubbles, mute other players.
    internal class ChatHud : OvertimePanel
    {
        private static ChatHud current;

        // a player on this PC: their character (MatchSync.Players index), label and device
        private class Local
        {
            public int Char;
            public string Label;
            public SlotInput Input;
            public float NextAt;
            public int Wheel = -1;                  // 0 quick, 1 emote, -1 closed
            public int Sel;
            public GameObject WheelRoot;            // each player's own wheel (local players can chat at once)
        }

        private class Bubble { public int Char; public SpriteRenderer Box; public TextMeshPro Text; public float Until, Start; public bool Emote; }
        private class LogLine { public string Text; public float At; }

        private readonly List<Local> locals = new List<Local>();
        private readonly Dictionary<int, Bubble> bubbles = new Dictionary<int, Bubble>();
        private static readonly List<LogLine> log = new List<LogLine>();
        private TextMeshPro logText, hint;
        private SpriteRenderer logBack;
        private Field field;
        private Local typer;                       // who the chat box is for
        private PlayerController[] chars = new PlayerController[4];
        private float charsAt = -1f;
        private static float closedAt = -10f;      // the chat box / a wheel just closed: Esc / START don't pause
        private static bool hintShown;

        public static bool Busy => current != null && (current.typer != null || current.locals.Any(l => l.Wheel >= 0));
        public static bool JustClosed => Time.unscaledTime - closedAt < 0.3f;

        // ------------------------------------------------------------ life

        // NetRunner, every frame: one HUD per match scene
        public static void Tick()
        {
            if (current != null) return;
            if (TitleController.onTitle || TutorialManager.onTutorial || GameManager.Instance == null || Camera.main == null || Online.Replays.Playing) return;
            if (SceneManager.GetActiveScene().name != "Game") return;
            var go = new GameObject("Overtime Chat");
            current = go.AddComponent<ChatHud>();
            if (!current.InitKit(240)) { Destroy(go); current = null; return; }
            current.Build();
        }

        private void Build()
        {
            float left = -W / 2f + 1f, bottom = -H / 2f + 1f;
            logText = Text("", new Vector2(left + 11f, bottom + 5f), 0.52f, 22f, Color.white, TextAlignmentOptions.BottomLeft, o: 20, height: 8f, wrap: true);
            logBack = Box("Log", new Vector2(left + 11f, bottom + 1f), new Vector2(23f, 1f), new Color(0.02f, 0.12f, 0.22f, 0.7f), 19);
            logBack.enabled = false;
            field = TextField(new Vector2(0f, bottom + 0.4f), new Vector2(W * 0.5f, 1.6f), $"SAY SOMETHING  ({Controls.KeyName(Controls.ChatKey)}: SEND · ESC: CANCEL)", ChatDef.MaxLength, Submit);
            field.Box.gameObject.SetActive(false); field.Text.gameObject.SetActive(false);
            stops.Clear();                          // nothing here takes the pad's menu navigation
            if (!hintShown)
            {
                hintShown = true;
                var k = Controls.KeyName(Controls.KeyOf(0, KeyAct.QuickChat)); var e = Controls.KeyName(Controls.KeyOf(0, KeyAct.Emote));
                AddLog($"<color=#9FE8FF>CHAT: {k} QUICK CHAT · {e} EMOTE · {Controls.KeyName(Controls.ChatKey)} TYPE (OTHER KEY SETS: SEE CONTROLS) · PAD: D-PAD UP / DOWN, SELECT</color>");
            }
        }

        protected override void OnDestroy() { base.OnDestroy(); if (current == this) current = null; }

        // ------------------------------------------------------------ who plays here

        // this PC's players, rebuilt every frame (seats and gamepads change); a player keeps their wheel and cooldown
        private readonly Dictionary<string, Local> previous = new Dictionary<string, Local>();

        private static string KeyOf(int ch, string label, SlotInput i) => label + "|" + ch + "|" + Net.KeyOf(i);

        private void Add(int ch, string label, SlotInput input)
        {
            if (input == null) return;
            var k = KeyOf(ch, label, input);
            locals.Add(previous.TryGetValue(k, out var old) ? old : new Local { Char = ch, Label = label, Input = input });
            previous.Remove(k);
        }

        private void FindLocals()
        {
            previous.Clear();
            foreach (var l in locals) previous[KeyOf(l.Char, l.Label, l.Input)] = l;
            locals.Clear();
            Collect();
            foreach (var gone in previous.Values) if (gone.WheelRoot != null) Destroy(gone.WheelRoot);   // a player who left keeps no wheel
        }

        private void Collect()
        {
            if (VersusState.Active)
            {
                foreach (var s in VersusState.Setup.Slots.Where(x => x.Control == SlotControl.Human && x.Input != null))
                {
                    int ch = s.Side * 2 + (s.Role == "hitter" ? 0 : 1);
                    string label = Net.Online ? $"{Net.MyName} (P{s.Number})" : $"P{s.Number}";
                    if (s.Input.Kind != InputKind.Remote) { if (!Net.IsClient) Add(ch, label, s.Input); }
                    else
                    {
                        int mine = Net.MyDevice(s.Input);
                        if (mine < 0) continue;
                        Add(ch, label, mine >= 1000 ? new SlotInput { Kind = InputKind.Gamepad, DeviceId = mine - 1000 } : new SlotInput { Kind = (InputKind)mine });
                    }
                }
                return;
            }
            // runs: co-op is player 1 on key set 2 + the 1st gamepad, player 2 on key set 1 + the 2nd; solo is player 1 on everything
            bool coop = GameManager.Instance.numberOfPlayers == 2;
            var pads = Gamepad.all.ToList();
            SlotInput Pad(int i) => i < pads.Count ? new SlotInput { Kind = InputKind.Gamepad, DeviceId = pads[i].deviceId } : null;
            if (coop)
            {
                Add(0, "P1", new SlotInput { Kind = InputKind.KeysArrows }); Add(0, "P1", Pad(0));
                Add(1, "P2", new SlotInput { Kind = InputKind.KeysWASD }); Add(1, "P2", Pad(1));
            }
            else
            {
                Add(0, "YOU", new SlotInput { Kind = InputKind.KeysWASD }); Add(0, "YOU", new SlotInput { Kind = InputKind.KeysArrows });
                for (int i = 0; i < pads.Count; i++) Add(0, "YOU", Pad(i));
            }
        }

        private PlayerController CharOf(int i)
        {
            if (Time.unscaledTime - charsAt > 0.5f) { chars = MatchSync.Players(); charsAt = Time.unscaledTime; }
            return i >= 0 && i < chars.Length ? chars[i] : null;
        }

        // a character a wheel or the chat box on this PC has taken over
        public static bool Blocked(PlayerController pc)
        {
            var h = current;
            if (h == null || pc == null) return false;
            foreach (var l in h.locals)
                if ((l.Wheel >= 0 || h.typer == l || (h.typer != null && l.Input.Kind != InputKind.Gamepad && h.typer.Input.Kind != InputKind.Gamepad)) && h.CharOf(l.Char) == pc) return true;
            return false;
        }

        // an online client: this device's presses don't go to the host while its player chats
        public static bool DeviceBusy(int key)
        {
            var h = current;
            if (h == null) return false;
            if (h.typer != null && key < 1000 && h.typer.Input.Kind != InputKind.Gamepad) return true;
            return h.locals.Any(l => (l.Wheel >= 0 || h.typer == l) && Net.KeyOf(l.Input) == key);
        }

        // ------------------------------------------------------------ input

        private void Update()
        {
            FindLocals();
            var kb = Keyboard.current;
            if (typer != null)
            {
                if (!locals.Contains(typer)) CloseBox();
                else if (OnScreenKeyboard.IsOpen) { }
                else if (!IsFocused(field)) CloseBox();             // Esc in FieldKeys
                else FieldKeys();
            }
            else
            {
                foreach (var l in locals)
                {
                    var s = VersusInput.Read(l.Input);
                    if (l.Wheel >= 0) { WheelInput(l, s); continue; }
                    bool chatKey = l.Input.Kind != InputKind.Gamepad && kb != null && kb[Controls.ChatKey].wasPressedThisFrame && l == locals.First(x => x.Input.Kind != InputKind.Gamepad);
                    if (s.QuickChat) OpenWheel(l, 0);
                    else if (s.Emote) OpenWheel(l, 1);
                    else if (s.Chat || chatKey) OpenBox(l);
                }
            }
            ShowLog();
        }

        private void WheelInput(Local l, PadState s)
        {
            int n = l.Wheel == 0 ? ChatDef.QuickText.Length : ChatDef.EmoteText.Length;
            int before = l.Sel;
            if (l.Input.Kind == InputKind.Gamepad)
            {
                var g = Gamepad.all.FirstOrDefault(x => x.deviceId == l.Input.DeviceId);
                var v = g != null ? (g.leftStick.ReadValue().sqrMagnitude > 0.4f ? g.leftStick.ReadValue() : g.dpad.ReadValue()) : Vector2.zero;
                if (v.sqrMagnitude > 0.4f)
                {
                    float ang = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;            // 0 = up, clockwise
                    l.Sel = Mathf.RoundToInt((ang + 360f) % 360f / (360f / n)) % n;
                }
            }
            else
            {
                if (s.Right) l.Sel = (l.Sel + 1) % n;
                if (s.Left) l.Sel = (l.Sel + n - 1) % n;
            }
            if (l.Sel != before) DrawWheel(l);
            // a gamepad's D-pad both picks and opens the wheels, so on a pad only B / RECEIVE close
            bool pad = l.Input.Kind == InputKind.Gamepad;
            bool send = s.Up || s.Confirm;
            bool close = s.Back || s.Down || (!pad && (l.Wheel == 0 ? s.QuickChat : s.Emote));
            if (send) { int kind = l.Wheel; CloseWheel(l); Say(l, kind, l.Sel, null); }
            else if (close) CloseWheel(l);
            else if (!pad && l.Wheel == 0 && s.Emote) OpenWheel(l, 1);
            else if (!pad && l.Wheel == 1 && s.QuickChat) OpenWheel(l, 0);
        }

        private void OpenWheel(Local l, int kind)
        {
            // the same player on another device (a solo run reads every key set and pad) closes theirs
            foreach (var o in locals) if (o != l && o.Wheel >= 0 && o.Char == l.Char) CloseWheel(o);
            l.Wheel = kind; l.Sel = 0;
            DrawWheel(l);
        }

        private void CloseWheel(Local l)
        {
            l.Wheel = -1;
            closedAt = Time.unscaledTime;
            if (l.WheelRoot != null) { Destroy(l.WheelRoot); l.WheelRoot = null; }
        }

        private void DrawWheel(Local l)
        {
            if (l.WheelRoot != null) Destroy(l.WheelRoot);
            l.WheelRoot = new GameObject("Wheel " + l.Label);
            l.WheelRoot.transform.SetParent(transform, false);
            var labels = l.Wheel == 0 ? ChatDef.QuickLabel : ChatDef.EmoteLabel;
            var colors = l.Wheel == 0 ? ChatDef.QuickColor : ChatDef.EmoteColor;
            var centre = ScreenPosOf(l.Char, 4f) ?? Vector2.zero;
            centre = new Vector2(Mathf.Clamp(centre.x, -W / 2f + 10f, W / 2f - 10f), Mathf.Clamp(centre.y, -H / 2f + 9f, H / 2f - 8f));
            var parts = new List<Transform>();
            parts.Add(Box("Wheel", centre, new Vector2(17f, 14f), new Color(0.02f, 0.15f, 0.26f, 0.88f), 30).transform);
            parts.Add(Text($"{l.Label}\n{(l.Wheel == 0 ? "QUICK CHAT" : "EMOTE")}", centre, 0.5f, 7f, new Color(1f, 0.85f, 0.35f), o: 33).transform);
            int n = labels.Length;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var at = centre + new Vector2(Mathf.Sin(a) * 6.2f, Mathf.Cos(a) * 5.1f);
                bool on = i == l.Sel;
                parts.Add(Box("Item", at, new Vector2(5.2f, 1.8f), on ? new Color(0.96f, 0.55f, 0.12f, 1f) : new Color(0.16f, 0.6f, 0.86f, 1f), 31).transform);
                parts.Add(Text($"<color=#{(on ? "FFFFFF" : colors[i])}>{labels[i]}</color>", at, l.Wheel == 0 ? 0.46f : 0.64f, 5f, Color.white, o: 34).transform);
            }
            bool pad = l.Input.Kind == InputKind.Gamepad;
            parts.Add(Text(pad ? "STICK: CHOOSE · A: SEND · B: CLOSE" : "LEFT / RIGHT: CHOOSE · JUMP: SEND · RECEIVE: CLOSE", centre - new Vector2(0f, 7.7f), 0.42f, 19f, new Color(0.85f, 0.95f, 1f), o: 33).transform);
            foreach (var p in parts) p.SetParent(l.WheelRoot.transform, true);
        }

        private void OpenBox(Local l)
        {
            typer = l;
            SetField(field, "");
            field.Box.gameObject.SetActive(true); field.Text.gameObject.SetActive(true);
            if (l.Input.Kind == InputKind.Gamepad) OnScreenKeyboard.Open(field, this);
            else Focus(field);
        }

        private void CloseBox()
        {
            typer = null;
            closedAt = Time.unscaledTime;
            if (IsFocused(field)) Focus(null);
            field.Box.gameObject.SetActive(false); field.Text.gameObject.SetActive(false);
        }

        private void Submit(string text)
        {
            var who = typer;
            CloseBox();
            text = (text ?? "").Trim();
            if (who == null || text.Length == 0) return;
            Say(who, 2, 0, text);
        }

        // development (DevOnline -ovt-chat): this PC's first player says something
        public static void DevSay(int n)
        {
            var h = current;
            if (h == null || h.locals.Count == 0) return;
            var l = h.locals[0];
            l.NextAt = 0f;
            if (n % 3 == 2) h.Say(l, KindText, 0, "test line " + n + " from " + (Net.IsHost ? "the host" : "a client"));
            else h.Say(l, n % 3, n % 4, null);
        }

        // ------------------------------------------------------------ sending and showing

        private const int KindQuick = 0, KindEmote = 1, KindText = 2;

        private void Say(Local l, int kind, int index, string text)
        {
            if (Time.unscaledTime < l.NextAt) { AddLog("<color=#7F93A8>(WAIT A MOMENT...)</color>"); return; }
            l.NextAt = Time.unscaledTime + (kind == KindText ? ChatDef.TypedCooldown : ChatDef.Cooldown);
            if (text != null && text.Length > ChatDef.MaxLength) text = text.Substring(0, ChatDef.MaxLength);
            if (!Net.Online) { Show(l.Char, l.Label, kind, index, text, true); return; }
            var w = new NetWriter(Msg.MatchChat).U8(l.Char).S(l.Label).U8(kind).U8(index).S(text ?? "").U8(Net.MyId);
            if (Net.IsHost) { Net.Send(w); Show(l.Char, l.Label, kind, index, text, true); }
            else Net.Send(w);                                      // the host's relay comes back to this PC too
        }

        // host: a friend's line, relayed to every PC (the sender's too, which shows it then) and shown here
        public static void Received(NetReader r, int fromPc)
        {
            int ch = r.U8(); string label = r.S(); int kind = r.U8(); int index = r.U8(); string text = r.S(); r.U8();
            if (label.Length > 40) label = label.Substring(0, 40);
            if (text.Length > ChatDef.MaxLength) text = text.Substring(0, ChatDef.MaxLength);
            if (ch > 3 || kind > 2) return;
            Net.Send(new NetWriter(Msg.MatchChat).U8(ch).S(label).U8(kind).U8(index).S(text).U8(fromPc));
            current?.Show(ch, label, kind, index, text, false);
        }

        // the client's own lines come back from the host with its PC id
        public static void ReceivedRelay(NetReader r)
        {
            int ch = r.U8(); string label = r.S(); int kind = r.U8(); int index = r.U8(); string text = r.S(); int pc = r.U8();
            current?.Show(ch, label, kind, index, text, pc == Net.MyId);
        }

        private void Show(int ch, string label, int kind, int index, string text, bool mine)
        {
            if (!mine && Net.Online && Plugin.ChatMuteOthers.Value) return;
            string body, color;
            if (kind == KindQuick && index < ChatDef.QuickText.Length) { body = ChatDef.QuickText[index]; color = ChatDef.QuickColor[index]; }
            else if (kind == KindEmote && index < ChatDef.EmoteText.Length) { body = ChatDef.EmoteText[index]; color = ChatDef.EmoteColor[index]; }
            else if (kind == KindText) { body = text ?? ""; color = "FFFFFF"; }
            else return;
            string safe = "<noparse>" + body.Replace("</noparse>", "") + "</noparse>";
            AddLog($"<color=#FFD95A><noparse>{label}</noparse>:</color> <color=#{color}>{safe}</color>");
            if (Plugin.ChatBubbles.Value) MakeBubble(ch, safe, color, kind == KindEmote);
            Plugin.Log.LogInfo($"Chat: {label} ({(kind == KindQuick ? "quick" : kind == KindEmote ? "emote" : "typed")}): {body}");
        }

        private void MakeBubble(int ch, string text, string color, bool emote)
        {
            if (bubbles.TryGetValue(ch, out var old)) { if (old.Box != null) Destroy(old.Box.gameObject); if (old.Text != null) Destroy(old.Text.gameObject); }
            float size = emote ? 1.4f : 0.66f, maxW = emote ? 7f : 13f;
            var t = Text($"<color=#{color}>{text}</color>", Vector2.zero, size, maxW, Color.white, o: 26, height: 4f, wrap: !emote);
            t.ForceMeshUpdate();
            var pref = t.GetPreferredValues(t.text, maxW / t.transform.localScale.x, 0f);
            var w = Mathf.Min(maxW, pref.x * t.transform.localScale.x) + 1.2f;
            var h = pref.y * t.transform.localScale.y + 0.8f;
            t.rectTransform.sizeDelta = new Vector2((w - 1.2f) / t.transform.localScale.x, (h - 0.6f) / t.transform.localScale.y);
            var box = Box("Bubble", Vector2.zero, new Vector2(w, h), emote ? new Color(0.02f, 0.15f, 0.26f, 0.0f) : new Color(0.02f, 0.15f, 0.26f, 0.85f), 25);
            bubbles[ch] = new Bubble { Char = ch, Box = box, Text = t, Start = Time.unscaledTime, Until = Time.unscaledTime + (emote ? ChatDef.EmoteSeconds : ChatDef.BubbleSeconds), Emote = emote };
        }

        // a character's spot on this panel (its head plus `up`), or null
        private Vector2? ScreenPosOf(int ch, float up)
        {
            var pc = CharOf(ch);
            var cam = Camera.main;
            if (pc == null || cam == null) return null;
            var local = transform.InverseTransformPoint(pc.transform.position + new Vector3(0f, up, 0f));
            return new Vector2(local.x, local.y);
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            foreach (var b in bubbles.Values.ToList())
            {
                float now = Time.unscaledTime;
                if (now > b.Until || b.Box == null)
                {
                    if (b.Box != null) Destroy(b.Box.gameObject);
                    if (b.Text != null) Destroy(b.Text.gameObject);
                    bubbles.Remove(b.Char);
                    continue;
                }
                var at = ScreenPosOf(b.Char, 9f + b.Box.size.y / 2f);
                if (at == null) continue;
                var p = new Vector2(Mathf.Clamp(at.Value.x, -W / 2f + b.Box.size.x / 2f, W / 2f - b.Box.size.x / 2f), Mathf.Clamp(at.Value.y, -H / 2f + 2f, H / 2f - b.Box.size.y));
                float pop = Mathf.Clamp01((now - b.Start) / 0.15f), fade = Mathf.Clamp01((b.Until - now) / 0.5f);
                float bounce = b.Emote ? Mathf.Abs(Mathf.Sin((now - b.Start) * 6f)) * 0.6f * fade : 0f;
                b.Box.transform.localPosition = new Vector3(p.x, p.y + bounce, b.Box.transform.localPosition.z);
                b.Text.transform.localPosition = new Vector3(p.x, p.y + bounce, b.Text.transform.localPosition.z);
                b.Box.transform.localScale = Vector3.one * (0.6f + 0.4f * pop);
                b.Text.alpha = fade;
                var c = b.Box.color; b.Box.color = new Color(c.r, c.g, c.b, b.Emote ? 0f : 0.85f * fade);
            }
        }

        private static void AddLog(string line)
        {
            log.Add(new LogLine { Text = line, At = Time.unscaledTime });
            while (log.Count > 30) log.RemoveAt(0);
        }

        private void ShowLog()
        {
            if (logText == null) return;
            bool open = typer != null;
            var recent = log.Where(l => open || Time.unscaledTime - l.At < ChatDef.LogSeconds).ToList();
            var shown = recent.Skip(Math.Max(0, recent.Count - ChatDef.LogLines));
            string s = string.Join("\n", shown.Select(l => l.Text));
            if (logText.text == s) return;
            logText.text = s;
            // the backdrop behind the lines (the floor under the corner is light)
            logBack.enabled = s.Length > 0;
            if (s.Length == 0) return;
            logText.ForceMeshUpdate();
            float hgt = logText.GetPreferredValues(s, logText.rectTransform.sizeDelta.x, 0f).y * logText.transform.localScale.y + 0.6f;
            float bottomY = logText.transform.localPosition.y - 4f;                    // the text box's bottom edge (height 8)
            logBack.size = new Vector2(23f, hgt);
            logBack.transform.localPosition = new Vector3(logBack.transform.localPosition.x, bottomY + hgt / 2f - 0.3f, logBack.transform.localPosition.z);
        }

        // ------------------------------------------------------------ hooks

        // a character taken over by a wheel or the chat box gets no gameplay input
        [HarmonyPatch(typeof(PlayerController), "SetXInput")]
        private static class HoldMove
        {
            [HarmonyPrefix]
            private static void Prefix(PlayerController __instance, ref float input) { if (Blocked(__instance)) input = 0f; }
        }

        [HarmonyPatch(typeof(PlayerController), "UpInputPressed")]
        private static class HoldUp
        {
            [HarmonyPrefix]
            private static bool Prefix(PlayerController __instance) => !Blocked(__instance);
        }

        [HarmonyPatch(typeof(PlayerController), "DownInputPressed")]
        private static class HoldDown
        {
            [HarmonyPrefix]
            private static bool Prefix(PlayerController __instance) => !Blocked(__instance);
        }

        // Esc / START that closes the chat box or a wheel doesn't also pause
        [HarmonyPatch(typeof(PauseController), "Pause_Unpause")]
        private static class HoldPause
        {
            [HarmonyPrefix]
            private static bool Prefix() => !Busy && !JustClosed && !OnScreenKeyboard.IsOpen && !Online.Replays.Playing;
        }
    }
}

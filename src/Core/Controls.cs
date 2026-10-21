using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace HangtimeOvertime.Core
{
    // Saved key and button bindings (HangtimeOvertime_controls.json). Four keyboard key sets and four gamepad slots, so
    // every local player has their own controls:
    //  - Versus / online: a player joins with any key set or gamepad, then plays with that set's / pad slot's bindings
    //    (gamepad slot = the pad's order among connected pads);
    //  - Classic, Infinite (the game's own input): key sets 1 and 2 both control you (the game's WASD and arrows), any pad;
    //  - Co-op: key set 2 = player 1, key set 1 = player 2 (the game's split), pads as the game assigns them.
    [Serializable]
    public class ControlsData
    {
        public int version = 1;
        public string[] keys = new string[0];      // set * KeyActs + action: a Key name ("W", "LeftArrow")
        public string[] pads = new string[0];      // slot * PadActs + action: a control name ("buttonSouth", "dpad/up")
        public string chatKey = "Enter";           // typed chat (in matches)
    }

    internal enum KeyAct { Left, Right, Jump, Receive, Confirm, QuickChat, Emote }
    internal enum PadAct { Jump, JumpAlt, Receive, ReceiveAlt, Confirm, Back, QuickChat, Emote, Chat }

    internal static class Controls
    {
        public const int Sets = 4, Slots = 4;
        public static readonly int KeyActs = Enum.GetValues(typeof(KeyAct)).Length, PadActs = Enum.GetValues(typeof(PadAct)).Length;
        public static readonly string[] KeyActNames = { "LEFT", "RIGHT", "JUMP / HIT", "RECEIVE", "MENU OK", "QUICK CHAT", "EMOTE" };
        public static readonly string[] PadActNames = { "JUMP / HIT", "JUMP (2ND)", "RECEIVE", "RECEIVE (2ND)", "MENU OK", "BACK", "QUICK CHAT", "EMOTE", "TYPED CHAT" };
        public static event Action Changed;

        private static readonly Key[,] DefaultKeys =
        {
            { Key.A, Key.D, Key.W, Key.S, Key.Space, Key.Q, Key.E },
            { Key.LeftArrow, Key.RightArrow, Key.UpArrow, Key.DownArrow, Key.Enter, Key.RightShift, Key.RightCtrl },
            { Key.J, Key.L, Key.I, Key.K, Key.H, Key.U, Key.O },
            { Key.Numpad4, Key.Numpad6, Key.Numpad8, Key.Numpad5, Key.NumpadEnter, Key.Numpad7, Key.Numpad9 },
        };
        private static readonly string[] DefaultPad = { "buttonSouth", "rightTrigger", "buttonEast", "rightShoulder", "buttonSouth", "buttonEast", "dpad/up", "dpad/down", "select" };

        private static ControlsData data;
        private static string PathOf => Path.Combine(Application.persistentDataPath, "HangtimeOvertime_controls.json");

        public static ControlsData Data { get { Load(); return data; } }

        public static void Load()
        {
            if (data != null) return;
            try { data = File.Exists(PathOf) ? JsonUtility.FromJson<ControlsData>(File.ReadAllText(PathOf)) : null; }
            catch (Exception e) { Plugin.Log.LogWarning("Controls file unreadable, using defaults: " + e.Message); }
            if (data == null) data = new ControlsData();
            if (data.keys.Length != Sets * KeyActs) { var k = data.keys; data.keys = new string[Sets * KeyActs]; for (int i = 0; i < data.keys.Length; i++) data.keys[i] = i < k.Length && Valid(k[i]) ? k[i] : DefaultKeys[i / KeyActs, i % KeyActs].ToString(); }
            if (data.pads.Length != Slots * PadActs) { var p = data.pads; data.pads = new string[Slots * PadActs]; for (int i = 0; i < data.pads.Length; i++) data.pads[i] = i < p.Length && !string.IsNullOrEmpty(p[i]) ? p[i] : DefaultPad[i % PadActs]; }
            if (!Valid(data.chatKey)) data.chatKey = "Enter";
        }

        private static bool Valid(string key) => !string.IsNullOrEmpty(key) && Enum.TryParse<Key>(key, out var k) && k != Key.None;

        public static void Save()
        {
            try
            {
                var tmp = PathOf + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(Data, true));
                if (File.Exists(PathOf)) File.Delete(PathOf);
                File.Move(tmp, PathOf);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't save controls: " + e.Message); }
            Changed?.Invoke();
        }

        // ------------------------------------------------------------ keyboard

        public static Key KeyOf(int set, KeyAct a) => Enum.TryParse<Key>(Data.keys[set * KeyActs + (int)a], out var k) ? k : DefaultKeys[set, (int)a];
        public static void SetKey(int set, KeyAct a, Key k) { Data.keys[set * KeyActs + (int)a] = k.ToString(); Save(); }
        public static Key ChatKey => Enum.TryParse<Key>(Data.chatKey, out var k) ? k : Key.Enter;
        public static void SetChatKey(Key k) { Data.chatKey = k.ToString(); Save(); }

        public static KeyControl Control(Keyboard kb, int set, KeyAct a) => kb[KeyOf(set, a)];

        // "W A S D" from the set's current left / jump / receive / right keys
        public static string SetLabel(int set)
        {
            var kb = Keyboard.current;
            string N(KeyAct a) => kb != null ? Short(kb[KeyOf(set, a)].displayName) : KeyOf(set, a).ToString();
            return $"{N(KeyAct.Jump)} {N(KeyAct.Left)} {N(KeyAct.Receive)} {N(KeyAct.Right)}".ToUpperInvariant();
        }

        public static string KeyName(Key k)
        {
            var kb = Keyboard.current;
            return (kb != null ? kb[k].displayName : k.ToString()).ToUpperInvariant();
        }

        private static string Short(string n) => n.Replace("Arrow", "").Replace("Numpad ", "NUM").Replace("Num ", "NUM").Trim();

        // ------------------------------------------------------------ gamepad

        public static string PadOf(int slot, PadAct a) => Data.pads[Mathf.Clamp(slot, 0, Slots - 1) * PadActs + (int)a];
        public static void SetPad(int slot, PadAct a, string control) { Data.pads[slot * PadActs + (int)a] = control; Save(); }

        public static ButtonControl Button(Gamepad g, int slot, PadAct a) => g == null ? null : g.TryGetChildControl<ButtonControl>(PadOf(slot, a));

        public static int SlotOf(Gamepad g) => Mathf.Clamp(Gamepad.all.ToList().IndexOf(g), 0, Slots - 1);

        public static string PadName(string control) => control switch
        {
            "buttonSouth" => "A / CROSS", "buttonEast" => "B / CIRCLE", "buttonWest" => "X / SQUARE", "buttonNorth" => "Y / TRIANGLE",
            "leftShoulder" => "LB / L1", "rightShoulder" => "RB / R1", "leftTrigger" => "LT / L2", "rightTrigger" => "RT / R2",
            "start" => "START", "select" => "SELECT / VIEW", "leftStickPress" => "L3", "rightStickPress" => "R3",
            "dpad/up" => "D-PAD UP", "dpad/down" => "D-PAD DOWN", "dpad/left" => "D-PAD LEFT", "dpad/right" => "D-PAD RIGHT",
            _ => control.ToUpperInvariant(),
        };

        // the buttons a gamepad action can be bound to
        public static readonly string[] PadControls = { "buttonSouth", "buttonEast", "buttonWest", "buttonNorth", "leftShoulder", "rightShoulder", "leftTrigger", "rightTrigger", "select", "leftStickPress", "rightStickPress", "dpad/up", "dpad/down", "dpad/left", "dpad/right" };

        // ------------------------------------------------------------ resetting

        public static void ResetSet(int set) { for (int a = 0; a < KeyActs; a++) Data.keys[set * KeyActs + a] = DefaultKeys[set, a].ToString(); Save(); }
        public static void ResetSlot(int slot) { for (int a = 0; a < PadActs; a++) Data.pads[slot * PadActs + a] = DefaultPad[a]; Save(); }
        public static void ResetAll()
        {
            data = null;
            try { if (File.Exists(PathOf)) File.Delete(PathOf); } catch { }
            Load();
            Save();
        }

        // where else a key is used (key sets and the chat key), for the conflict warning
        public static string KeyUsedBy(Key k, int exceptSet, int exceptAct, bool exceptChat)
        {
            for (int s = 0; s < Sets; s++)
                for (int a = 0; a < KeyActs; a++)
                    if (!(s == exceptSet && a == exceptAct) && KeyOf(s, (KeyAct)a) == k) return $"KEYS {s + 1} · {KeyActNames[a]}";
            if (!exceptChat && ChatKey == k) return "TYPED CHAT";
            return null;
        }

        public static string PadUsedBy(int slot, string control, int exceptAct)
        {
            for (int a = 0; a < PadActs; a++)
            {
                if (a == exceptAct || PadOf(slot, (PadAct)a) != control) continue;
                // menu buttons share buttons with play on purpose (they are never used at the same time)
                bool menuPair = (a == (int)PadAct.Confirm || a == (int)PadAct.Back || exceptAct == (int)PadAct.Confirm || exceptAct == (int)PadAct.Back);
                if (!menuPair) return $"PAD {slot + 1} · {PadActNames[a]}";
            }
            return null;
        }
    }
}

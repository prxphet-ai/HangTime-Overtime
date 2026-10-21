using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace HangtimeOvertime.Debugging
{
    // Development (DevKeys on, -ovt-devpad <file>): a virtual gamepad driven by a command file, so gamepad-only tests can
    // run without a controller and without window focus. Each line written to the file runs once, then the file is
    // emptied: "tap south", "tap dpad/down", "hold rightTrigger 300", "stick -1 0 500", "wait 200".
    internal class DevPad : MonoBehaviour
    {
        private Gamepad pad;
        private string file;
        private readonly Queue<string> queue = new Queue<string>();
        private float busyUntil;
        private uint buttons;
        private Vector2 stick;
        private float lt, rt, releaseAt = -1f;

        public static void Hook(GameObject on)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-ovt-devpad");
            if (i < 0 || i + 1 >= args.Length) return;
            var d = on.AddComponent<DevPad>();
            d.file = args[i + 1];
            Plugin.Log.LogInfo("Dev pad: reading " + d.file);
        }

        private void Update()
        {
            if (pad == null) { pad = InputSystem.AddDevice<Gamepad>("Overtime dev pad"); Plugin.Log.LogInfo($"Dev pad: added (gamepads: {Gamepad.all.Count})"); }
            try
            {
                if (File.Exists(file))
                {
                    var lines = File.ReadAllLines(file);
                    if (lines.Length > 0) { File.WriteAllText(file, ""); foreach (var l in lines) if (l.Trim().Length > 0) queue.Enqueue(l.Trim()); }
                }
            }
            catch { }
            if (releaseAt > 0f && Time.unscaledTime >= releaseAt) { buttons = 0; stick = Vector2.zero; lt = rt = 0f; releaseAt = -1f; Send(); }
            if (Time.unscaledTime < busyUntil || queue.Count == 0) return;
            var cmd = queue.Dequeue().Split(' ');
            float ms(int idx, float def) => cmd.Length > idx && float.TryParse(cmd[idx], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;
            switch (cmd[0])
            {
                case "tap": Press(cmd[1], 90f); break;
                case "hold": Press(cmd[1], ms(2, 300f)); break;
                case "stick": stick = new Vector2(ms(1, 0f), ms(2, 0f)); releaseAt = Time.unscaledTime + ms(3, 300f) / 1000f; busyUntil = releaseAt + 0.05f; Send(); break;
                case "wait": busyUntil = Time.unscaledTime + ms(1, 200f) / 1000f; break;
            }
            Plugin.Log.LogInfo("[devpad] " + string.Join(" ", cmd));
        }

        private void Press(string control, float holdMs)
        {
            switch (control)
            {
                case "leftTrigger": lt = 1f; break;
                case "rightTrigger": rt = 1f; break;
                default: buttons |= 1u << (int)Button(control); break;
            }
            releaseAt = Time.unscaledTime + holdMs / 1000f;
            busyUntil = releaseAt + 0.06f;
            Send();
        }

        private static GamepadButton Button(string c) => c switch
        {
            "south" or "buttonSouth" => GamepadButton.South, "east" or "buttonEast" => GamepadButton.East,
            "west" or "buttonWest" => GamepadButton.West, "north" or "buttonNorth" => GamepadButton.North,
            "leftShoulder" => GamepadButton.LeftShoulder, "rightShoulder" => GamepadButton.RightShoulder,
            "start" => GamepadButton.Start, "select" => GamepadButton.Select,
            "dpad/up" => GamepadButton.DpadUp, "dpad/down" => GamepadButton.DpadDown, "dpad/left" => GamepadButton.DpadLeft, "dpad/right" => GamepadButton.DpadRight,
            "leftStickPress" => GamepadButton.LeftStick, "rightStickPress" => GamepadButton.RightStick,
            _ => GamepadButton.South,
        };

        private void Send()
        {
            if (pad == null) return;
            InputSystem.QueueStateEvent(pad, new GamepadState { buttons = buttons, leftStick = stick, leftTrigger = lt, rightTrigger = rt });
        }

        private void OnDestroy() { if (pad != null) InputSystem.RemoveDevice(pad); }
    }
}

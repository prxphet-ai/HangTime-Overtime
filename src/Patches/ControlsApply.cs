using System.Linq;
using HangtimeOvertime.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HangtimeOvertime.Patches
{
    // The saved bindings (Controls) in the game's own input: its Gameplay actions get binding overrides (Classic,
    // Infinite: the arrow keys become key set 2, WASD key set 1; gamepad 1 / 2 the pad slots' jump and receive buttons),
    // and the co-op keyboard split reads key set 2 (player 1) and key set 1 (player 2). Versus reads Controls directly.
    internal static class ControlsApply
    {
        public static void Init() => Controls.Changed += Apply;

        public static void Apply()
        {
            var im = GameManager.Instance != null ? GameManager.Instance.inputManager : null;
            var map = im != null ? im.Gameplay : null;
            if (map == null) return;
            int n = 0;
            foreach (var action in map.actions)
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    var b = action.bindings[i];
                    string target = Target(action.name, b);
                    if (target == null) continue;
                    if (target == b.path) action.RemoveBindingOverride(i);
                    else action.ApplyBindingOverride(i, target);
                    n++;
                }
            Plugin.Log.LogInfo($"Controls: {n} of the game's bindings follow the saved controls");
        }

        // the saved control for one of the game's bindings (by action and its original path), or null to leave it alone
        private static string Target(string action, InputBinding b)
        {
            string p = b.path ?? "";
            string K(int set, KeyAct a) => "<Keyboard>/" + KeyPath(Controls.KeyOf(set, a));
            string P(int slot, PadAct a) => "<Gamepad>/" + Controls.PadOf(slot, a);
            bool arrows = action.EndsWith("P1");              // P1 = arrows (+ pad 1); P2 / P3 = WASD (P2 also pad 2)
            int set = arrows ? 1 : 0, slot = action.EndsWith("P2") ? 1 : 0;
            if (action.StartsWith("Move") && b.isPartOfComposite)
            {
                if (b.name == "Left" || b.name == "left") return K(set, KeyAct.Left);
                if (b.name == "Right" || b.name == "right") return K(set, KeyAct.Right);
                return null;
            }
            if (action.StartsWith("Up"))
            {
                if (p.StartsWith("<Keyboard>")) return K(set, KeyAct.Jump);
                if (p == "<Gamepad>/buttonSouth") return P(slot, PadAct.Jump);
                if (p == "<Gamepad>/rightTrigger") return P(slot, PadAct.JumpAlt);
            }
            if (action.StartsWith("Down"))
            {
                if (p.StartsWith("<Keyboard>")) return K(set, KeyAct.Receive);
                if (p == "<Gamepad>/buttonEast") return P(slot, PadAct.Receive);
                if (p == "<Gamepad>/rightShoulder") return P(slot, PadAct.ReceiveAlt);
            }
            return null;
        }

        // the input system's path for a key ("upArrow", "w", "numpad4")
        public static string KeyPath(Key k)
        {
            var kb = Keyboard.current;
            if (kb != null) { var c = kb[k]; if (c != null) return c.name; }
            string s = k.ToString();
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }

        [HarmonyPatch(typeof(InputManager), "Start")]
        private static class AtStart { [HarmonyPostfix] private static void Postfix() => Apply(); }

        // co-op's split keys
        [HarmonyPatch(typeof(ManualInputRouter), "Update")]
        private static class CoopKeys
        {
            [HarmonyPrefix]
            private static void Prefix(ManualInputRouter __instance)
            {
                __instance.p1Left = Controls.KeyOf(1, KeyAct.Left); __instance.p1Right = Controls.KeyOf(1, KeyAct.Right);
                __instance.p1Up = Controls.KeyOf(1, KeyAct.Jump); __instance.p1Down = Controls.KeyOf(1, KeyAct.Receive);
                __instance.p2Left = Controls.KeyOf(0, KeyAct.Left); __instance.p2Right = Controls.KeyOf(0, KeyAct.Right);
                __instance.p2Up = Controls.KeyOf(0, KeyAct.Jump); __instance.p2Down = Controls.KeyOf(0, KeyAct.Receive);
            }
        }
    }
}

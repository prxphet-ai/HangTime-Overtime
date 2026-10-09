using HangtimeOvertime.Engine;
using HarmonyLib;

namespace HangtimeOvertime.Patches
{
    // Every player gets a ControllerFx; stuns and landing lag gate the player's inputs (human or AI).
    internal static class ControllerPatches
    {
        [HarmonyPatch(typeof(PlayerController), "Start")]
        private static class Attach
        {
            [HarmonyPostfix]
            private static void Postfix(PlayerController __instance)
            {
                if (__instance.GetComponent<ControllerFx>() == null) __instance.gameObject.AddComponent<ControllerFx>();
            }
        }

        [HarmonyPatch(typeof(PlayerController), "SetXInput")]
        private static class Move
        {
            [HarmonyPrefix]
            private static void Prefix(PlayerController __instance, ref float input)
            {
                var fx = __instance.GetComponent<ControllerFx>();
                if (fx != null && fx.Stunned) input = 0f;
            }
        }

        [HarmonyPatch(typeof(PlayerController), "UpInputPressed")]
        private static class Up
        {
            [HarmonyPrefix]
            private static bool Prefix(PlayerController __instance)
            {
                Engine.Engine.CurrentInput = __instance;
                var fx = __instance.GetComponent<ControllerFx>();
                if (fx == null) return true;
                if (fx.Stunned) return false;
                return !(fx.Lagging && __instance.getGrounded() && !__instance.IsServing());
            }
        }

        [HarmonyPatch(typeof(PlayerController), "DownInputPressed")]
        private static class Down
        {
            [HarmonyPrefix]
            private static bool Prefix(PlayerController __instance)
            {
                var fx = __instance.GetComponent<ControllerFx>();
                return fx == null || !fx.Stunned;
            }
        }
    }
}

using HangtimeOvertime.Engine;
using HarmonyLib;

namespace HangtimeOvertime.Patches
{
    internal static class BallPatches
    {
        // After the game's own physics step (gravity is set there first): net crossing and flight effects.
        [HarmonyPatch(typeof(BallMovement), "FixedUpdate")]
        private static class Watch
        {
            [HarmonyPostfix]
            private static void Postfix(BallMovement __instance) => BallFx.Step(__instance);
        }
    }
}

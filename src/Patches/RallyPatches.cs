using HarmonyLib;

namespace HangtimeOvertime.Patches
{
    // Rally results feed perk triggers (streaks, match point) only; the score is never changed.
    internal static class RallyPatches
    {
        [HarmonyPatch(typeof(GameManager), "PlayerGotPoint")]
        private static class LeftWins
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (!TutorialManager.onTutorial) Engine.Engine.OnRallyEnd(0);
            }
        }

        [HarmonyPatch(typeof(GameManager), "OpponentGotPoint")]
        private static class RightWins
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (!TutorialManager.onTutorial) Engine.Engine.OnRallyEnd(1);
            }
        }
    }
}

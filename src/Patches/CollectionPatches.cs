using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HarmonyLib;

namespace HangtimeOvertime.Patches
{
    // Feeds the collection: a card on an upgrade screen is discovered, taking it counts a pick; the opponent of a
    // Classic / Co-op / Infinite match is discovered at kick-off (RunPatches.MatchStart) and beaten when the match is won.
    // Versus offers and picks are recorded in Versus.Offer / Versus.Take.
    internal static class CollectionPatches
    {
        [HarmonyPatch(typeof(TechniqueButton), "Start")]
        private static class PerkSeen
        {
            [HarmonyPostfix]
            private static void Postfix(TechniqueButton __instance)
            {
                var t = PerkRegistry.TechniqueOf(__instance.technique);
                if (t == null) return;
                Collection.CatalogPerk(t, __instance.technique.title, __instance.technique.description);
                Collection.See(Collection.KeyOf(t));
            }
        }

        [HarmonyPatch(typeof(TechniqueButton), "Click")]
        private static class PerkPicked
        {
            [HarmonyPostfix]
            private static void Postfix(TechniqueButton __instance) => Collection.Pick(Collection.KeyOf(PerkRegistry.TechniqueOf(__instance.technique)));
        }

        [HarmonyPatch(typeof(UpgradeButton), "Start")]
        private static class CardSeen
        {
            [HarmonyPostfix]
            private static void Postfix(UpgradeButton __instance)
            {
                Collection.CatalogCard(__instance.myPair);
                Collection.See(Collection.KeyOf(__instance.myPair));
            }
        }

        [HarmonyPatch(typeof(UpgradeButton), "Click")]
        private static class CardPicked
        {
            [HarmonyPostfix]
            private static void Postfix(UpgradeButton __instance) => Collection.Pick(Collection.KeyOf(__instance.myPair));
        }

        // kick-off of a run match: the opponent is discovered
        public static void MatchStarted(GameManager gm)
        {
            if (RunState.Mode == RunMode.Versus || TutorialManager.onTutorial) return;
            Collection.See(Collection.TeamKey(gm.opponentTeam?.teamName));
        }

        [HarmonyPatch(typeof(GameManager), "EndGame")]
        private static class TeamBeaten
        {
            [HarmonyPrefix]
            private static void Prefix(GameManager __instance, string nextScene)
            {
                if (__instance.done || VersusState.Active || TitleController.onTitle || TutorialManager.onTutorial || nextScene == "Lose") return;
                Collection.Beat(Collection.TeamKey(__instance.opponentTeam?.teamName));
            }
        }
    }
}

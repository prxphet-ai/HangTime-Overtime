using System.Collections.Generic;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HarmonyLib;

namespace HangtimeOvertime.Patches
{
    // The daily run's seed: the game's random numbers are seeded by the day and match for the kick-off (opponent team,
    // its level-ups and perks) and for building the upgrade screen's card pools; the offered cards themselves are the
    // first unowned ones in the day's order (Daily.Pick), given to the card buttons as they appear. Rallies stay random.
    internal static class DailyPatches
    {
        private static bool Active => RunState.Daily != null && RunState.Mode == RunMode.Infinite && !TitleController.onTitle;

        [HarmonyPatch(typeof(GameManager), "OnStart")]
        private static class SeedKickOff
        {
            [HarmonyPriority(Priority.First)]
            [HarmonyPrefix]
            private static void Prefix(out bool __state)
            {
                __state = Active;
                if (__state) Daily.Push(1, RunState.InfiniteMatch);
            }

            [HarmonyPriority(Priority.Last)]
            [HarmonyPostfix]
            private static void Postfix(bool __state) { if (__state) Daily.Pop(); }
        }

        private static Logo_technique limitBreak;

        [HarmonyPatch(typeof(UpgradeManager), "Start")]
        private static class SeedOffers
        {
            [HarmonyPriority(Priority.First)]
            [HarmonyPrefix]
            private static void Prefix(out bool __state)
            {
                __state = Active;
                Daily.PerkOffer.Clear(); Daily.CardOffer.Clear();
                if (__state) Daily.Push(2, RunState.InfiniteMatch);
            }

            // after the reward pools are built (UpgradePatches.Pool): the day's two perks and three stat cards
            [HarmonyPriority(Priority.Last)]
            [HarmonyPostfix]
            private static void Postfix(UpgradeManager __instance, bool __state)
            {
                if (!__state) return;
                var t = Traverse.Create(__instance);
                limitBreak = t.Field("limitBreakTechnique").GetValue<Logo_technique>();
                var owned = PerkRegistry.PlayerTechniques ?? new List<Technique>();
                var cards = t.Field("techniques").GetValue<List<Logo_technique>>();
                var pairs = t.Field("upgrades").GetValue<List<UpgradePair>>();
                if (cards != null)
                    foreach (var c in Daily.Pick(cards, c => Collection.KeyOf(PerkRegistry.TechniqueOf(c)) ?? c.title, c => !owned.Contains(PerkRegistry.TechniqueOf(c)), 2, 1))
                        Daily.PerkOffer.Enqueue(c);
                if (pairs != null)
                    foreach (var p in Daily.Pick(pairs, p => Collection.KeyOf(p) ?? p.title, p => true, 3, 2))
                        Daily.CardOffer.Enqueue(p);
                Daily.Pop();
                Plugin.Log.LogInfo($"Daily offers (match {RunState.InfiniteMatch}): perks {string.Join(", ", System.Linq.Enumerable.Select(Daily.PerkOffer, c => c.title))}; cards {string.Join(", ", System.Linq.Enumerable.Select(Daily.CardOffer, p => p.title))}");
            }
        }

        [HarmonyPatch(typeof(TechniqueButton), "Start")]
        private static class DailyPerk
        {
            [HarmonyPrefix]
            private static void Prefix(TechniqueButton __instance)
            {
                if (!Active || Daily.PerkOffer.Count == 0 || __instance.technique == limitBreak) return;
                __instance.technique = Daily.PerkOffer.Dequeue();
            }
        }

        [HarmonyPatch(typeof(UpgradeButton), "Start")]
        private static class DailyCard
        {
            [HarmonyPrefix]
            private static void Prefix(UpgradeButton __instance)
            {
                if (!Active || Daily.CardOffer.Count == 0) return;
                __instance.myPair = Daily.CardOffer.Dequeue();
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using TMPro;
using HarmonyLib;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // The upgrade screen: adds the Overtime perks to the card pool and, in infinite/loop runs,
    // picks which kind of reward is offered. The game decides perk/stat/limit-break cards from
    // gameNumber alone, so the screen gets a stand-in gameNumber and the real one is restored
    // before the game advances it.
    internal static class UpgradePatches
    {
        private enum Reward { Perk, Stat, LimitBreak }

        private static bool overridden;
        private static int realGameNumber;

        private static Reward RewardAt(int g, int lb) =>
            g == lb ? Reward.LimitBreak : (g <= lb ? (g + 1) % 2 == 0 : g % 2 == 0) ? Reward.Perk : Reward.Stat;

        [HarmonyPatch(typeof(UpgradeManager), "Start")]
        private static class Pool
        {
            [HarmonyPostfix]
            private static void Postfix(UpgradeManager __instance)
            {
                var t = Traverse.Create(__instance);
                var cards = t.Field("techniques").GetValue<List<Logo_technique>>();
                int lb = t.Field("limitBreakLevel").GetValue<int>();
                var lbCard = t.Field("limitBreakTechnique").GetValue<Logo_technique>();

                // Weighted pool: the picker draws uniformly from the list (by Capacity) and skips owned
                // or repeated cards, so each card appears as many times as its rarity weight.
                PerkRegistry.RememberVanilla(cards);
                var vanilla = cards.Where(c => !(PerkRegistry.TechniqueOf(c) is DataPerk)).Distinct().ToList();
                cards.Clear();
                if (!Debugging.DevKeys.OnlyOvertimeCards)
                    foreach (var c in vanilla) for (int i = 0; i < PerkRegistry.VanillaWeight(PerkRegistry.TechniqueOf(c)); i++) cards.Add(c);
                foreach (var perk in PerkRegistry.All)
                {
                    var card = PerkRegistry.MakeCard(perk);
                    for (int i = 0; i < PerkRegistry.Weight(perk); i++) cards.Add(card);
                }
                cards.Capacity = cards.Count;   // the game picks with Random.Range(0, Capacity)
                var statPool = t.Field("upgrades").GetValue<List<UpgradePair>>();
                StatCards.AddToPool(statPool);
                // the game walks a static index through this list; keep it inside the new size
                Traverse.Create(typeof(UpgradeManager)).Field("indexNum").SetValue(Random.Range(0, Mathf.Max(1, statPool.Count / 2)));
                if (Plugin.DumpScenes.Value) Debugging.Dumps.Upgrade(__instance, cards, lb);

                var owned = PerkRegistry.PlayerTechniques ?? new List<Technique>();
                int unowned = cards.Select(PerkRegistry.TechniqueOf).Distinct().Count(x => !owned.Contains(x));
                bool ownsLimitBreak = owned.Contains(PerkRegistry.TechniqueOf(lbCard));
                int g = GameManager.gameNumber;
                Reward gameWould = RewardAt(g, lb);
                Reward? want = null;

                if (RunState.Mode == RunMode.Infinite)
                {
                    int w = RunState.InfiniteWins;
                    if (w == lb && !ownsLimitBreak) want = Reward.LimitBreak;
                    else want = (w % 3 != 0 && unowned >= 2) ? Reward.Perk : Reward.Stat;
                }
                else if (gameWould == Reward.LimitBreak && ownsLimitBreak)
                {
                    want = unowned >= 2 ? Reward.Perk : Reward.Stat;   // loop 2+: no duplicate Limit Break
                }
                if (gameWould == Reward.Perk && unowned < 2 && want == null) want = Reward.Stat;  // picker would never finish
                if (want == Reward.Perk && unowned < 2) want = Reward.Stat;

                if (want == null || want == gameWould) return;
                int stand = Enumerable.Range(1, 8).OrderBy(x => x == 4 ? 1 : 0)
                    .FirstOrDefault(x => RewardAt(x, lb) == want.Value);
                if (stand == 0) return;
                realGameNumber = g;
                overridden = true;
                GameManager.gameNumber = stand;
                Plugin.Log.LogInfo($"Reward screen: {want} (stand-in match {stand}, real {g}, limit break at {lb}, {unowned} cards unowned)");
            }
        }

        [HarmonyPatch(typeof(UpgradeManager), "LoadGame")]
        private static class Leave
        {
            [HarmonyPrefix]
            private static void Prefix()
            {
                if (!overridden) return;
                GameManager.gameNumber = realGameNumber;
                overridden = false;
            }
        }

        [HarmonyPatch(typeof(TechniqueButton), "Start")]
        private static class CardStyle
        {
            private static readonly AccessTools.FieldRef<TechniqueButton, SpriteRenderer> art =
                AccessTools.FieldRefAccess<TechniqueButton, SpriteRenderer>("spriteRenderer");

            [HarmonyPostfix]
            private static void Postfix(TechniqueButton __instance)
            {
                if (!(PerkRegistry.TechniqueOf(__instance.technique) is DataPerk perk)) return;
                if (art(__instance) != null) art(__instance).color = PerkRegistry.ElementColor(perk);
                if (__instance.title != null) __instance.title.color = StatCards.RarityColor(perk.Def.Rarity);
            }
        }

        [HarmonyPatch(typeof(UpgradeButton), "Start")]
        private static class StatCardText
        {
            [HarmonyPostfix]
            private static void Postfix(UpgradeButton __instance)
            {
                var card = StatCards.CardOf(__instance.myPair);
                if (card == null) return;
                Traverse.Create(__instance).Field("fullDescription").SetValue(StatCards.LongText(card));
                if (__instance.title != null) __instance.title.color = StatCards.RarityColor(card.Rarity);
            }
        }

        // Overtime stat cards add card points; the rest is the game's own Click without the level-ups.
        [HarmonyPatch(typeof(UpgradeButton), "Click")]
        private static class StatCardClick
        {
            [HarmonyPrefix]
            private static bool Prefix(UpgradeButton __instance)
            {
                var card = StatCards.CardOf(__instance.myPair);
                if (card == null) return true;
                StatCards.Take(card);
                var t = Traverse.Create(__instance);
                UpgradeManager.gotUpgrade = true;
                t.Field("collected").GetValue<ParticleSystem>()?.Play();
                __instance.StartCoroutine(__instance.CollectedAnimation());
                t.Field("gotPicked").SetValue(true);
                __instance.description.text = t.Field("statNameDescription").GetValue<string>();
                return false;
            }
        }
    }
}

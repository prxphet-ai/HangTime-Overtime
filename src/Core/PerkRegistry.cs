using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Engine;
using HarmonyLib;
using UnityEngine;

namespace HangtimeOvertime.Core
{
    // One DataPerk per perks-sheet row, and the game's card entries (Logo_technique) for them,
    // reusing card art from the player's own copy of Hangtime!.
    internal static class PerkRegistry
    {
        private static readonly Dictionary<string, DataPerk> byId = new Dictionary<string, DataPerk>();
        private static readonly AccessTools.FieldRef<Logo_technique, Technique> logoTechnique =
            AccessTools.FieldRefAccess<Logo_technique, Technique>("myTechnique");

        // Vanilla card entries seen on the upgrade screen (art, names and opponent techniques come from here).
        public static readonly List<Logo_technique> VanillaCards = new List<Logo_technique>();

        public static IEnumerable<DataPerk> All => byId.Values;

        public static void Build()
        {
            foreach (var def in Generated.Perks.All)
            {
                var perk = ScriptableObject.CreateInstance<DataPerk>();
                perk.Init(def);
                byId[def.Id] = perk;
            }
            Plugin.Log.LogInfo($"{byId.Count} perks ready ({byId.Values.Count(p => p.Def.Origin == "new")} new)");
        }

        public static DataPerk Get(string id) => byId.TryGetValue(id, out var p) ? p : null;

        public static List<Technique> PlayerTechniques =>
            GameManager.Instance != null && GameManager.Instance.playerStats != null
                ? GameManager.Instance.playerStats.techniques : null;

        public static Technique TechniqueOf(Logo_technique card) => card == null ? null : logoTechnique(card);

        public static void RememberVanilla(List<Logo_technique> cards)
        {
            foreach (var c in cards)
                if (!(TechniqueOf(c) is DataPerk) && !VanillaCards.Any(v => TechniqueOf(v) == TechniqueOf(c)))
                    VanillaCards.Add(c);
        }

        public static Technique FindVanilla(string className)
        {
            var hit = VanillaCards.Select(TechniqueOf).FirstOrDefault(t => t != null && t.GetType().Name == className);
            return hit != null ? hit : Resources.FindObjectsOfTypeAll<Technique>().FirstOrDefault(t => t.GetType().Name == className);
        }

        public static string VanillaTitle(Technique t) =>
            VanillaCards.FirstOrDefault(v => TechniqueOf(v) == t)?.title ?? t.GetType().Name;

        public static Logo_technique MakeCard(DataPerk perk)
        {
            var card = new Logo_technique
            {
                title = perk.Def.Title,
                description = perk.Def.Description,
                flavourText = $"{perk.Def.Rarity.ToUpperInvariant()} {perk.Def.Element.ToUpperInvariant()}  \"{perk.Def.Flavour}\"".Replace(" NONE ", " "),
            };
            logoTechnique(card) = perk;
            var art = VanillaCards.FirstOrDefault(v => TechniqueOf(v)?.GetType().Name == perk.Def.ArtFrom);
            card.sprite = art?.sprite;
            if (card.sprite == null) Plugin.Log.LogWarning($"{perk.Def.Title}: no card art from {perk.Def.ArtFrom}");
            return card;
        }

        public static int Weight(DataPerk perk) => perk.Def.Rarity == "epic" ? 1 : perk.Def.Rarity == "rare" ? 2 : 3;
        // vanilla cards weigh by their rarity too (sheets/scaling.json vanilla_rarity): common 3, rare 2, epic 1
        public static int VanillaWeight(Technique t)
        {
            var r = t == null ? "rare" : Generated.Scaling.VanillaRarity(t.GetType().Name);
            return r == "epic" ? 1 : r == "rare" ? 2 : 3;
        }

        public static Color ElementColor(DataPerk perk) =>
            Vfx.Hex(Generated.Elements.All.FirstOrDefault(e => e.Id == perk.Def.Element).Color ?? "FFD27A");
    }
}

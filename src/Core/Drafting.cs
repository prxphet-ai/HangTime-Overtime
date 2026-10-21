using System;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Engine;
using HangtimeOvertime.Generated;
using UnityEngine;

namespace HangtimeOvertime.Core
{
    // One draft option: a team and the build it brings.
    public class DraftOption
    {
        public TeamDef Team;
        public readonly List<string> Perks = new List<string>();
        public readonly List<string> Cards = new List<string>();
        public float Power;                               // summed power of the parts
        public float SimWin;                              // the simulator's field win rate for the whole package (0: not played)
    }

    // The draft tournament: pick 1 of 3 teams, each with `Perks` abilities (some in its element) and `Cards` stat cards,
    // dealt so their simulated power (sheets/draft.json) is about the same; then an 8-match bracket where every opponent
    // has drafted too. Dealt like tools/draft.py deals them (that tool plays the packages in the simulator to check).
    internal static class Drafting
    {
        public static int PendingSeed;                    // the DRAFT menu: the next CLASSIC start is this draft
        public static DraftOption Pending;

        private static float PerkPower(string id) { int i = Array.IndexOf(Draft.PerkIds, id); return i >= 0 ? Draft.PerkPower[i] : 0f; }
        private static float CardPower(string id) { int i = Array.IndexOf(Draft.CardIds, id); return i >= 0 ? Draft.CardPower[i] : 0f; }

        private static string ElementOf(string perk) => Perks.All.FirstOrDefault(p => p.Id == perk)?.Element ?? "none";

        private static List<T> Sample<T>(List<T> from, int k, System.Random rng)
        {
            var copy = from.ToList();
            var outList = new List<T>();
            for (int i = 0; i < k && copy.Count > 0; i++) { int j = rng.Next(copy.Count); outList.Add(copy[j]); copy.RemoveAt(j); }
            return outList;
        }

        // perks (and cards) for a team of this element, redrawn until the power is near the target
        private static DraftOption DealOne(TeamDef team, string element, int perks, int cards, float target, System.Random rng)
        {
            var perkPool = Draft.PerkIds.Where((id, i) => Draft.PerkPower[i] > Draft.MinPower && PerkRegistry.Get(id) != null).ToList();
            var cardPool = Draft.CardIds.Where((id, i) => Draft.CardPower[i] > Draft.MinPower && Engine.StatCards.Get(id) != null).ToList();
            DraftOption best = null; float bestGap = float.MaxValue;
            for (int t = 0; t < Draft.Tries; t++)
            {
                var themed = perkPool.Where(p => ElementOf(p) == element).ToList();
                var pick = Sample(themed, Math.Min(Draft.ThemePerks, Math.Min(perks, themed.Count)), rng);
                pick.AddRange(Sample(perkPool.Where(p => !pick.Contains(p)).ToList(), perks - pick.Count, rng));
                var cpick = Sample(cardPool, cards, rng);
                float total = pick.Sum(PerkPower) + cpick.Sum(CardPower);
                float gap = Mathf.Abs(total - target);
                if (gap < bestGap)
                {
                    best = new DraftOption { Team = team, Power = total };
                    best.Perks.AddRange(pick); best.Cards.AddRange(cpick);
                    bestGap = gap;
                }
                if (gap <= Draft.Band) break;
            }
            return best;
        }

        // the three options of a draft: three different teams, one of each team's simulator-checked packages (sheets/
        // draft.json packages); without packages, dealt by summed power
        public static List<DraftOption> Deal(int seed)
        {
            var rng = new System.Random(seed);
            var withPackages = Draft.PackageTeam.Distinct().Select(TeamOf).Where(t => t != null).ToList();
            if (withPackages.Count >= Draft.Offers)
                return Sample(withPackages, Draft.Offers, rng).Select(t =>
                {
                    var mine = Enumerable.Range(0, Draft.PackageTeam.Length).Where(i => Draft.PackageTeam[i] == t.Id).ToList();
                    int i = mine[rng.Next(mine.Count)];
                    var o = new DraftOption { Team = t, Power = Draft.PackagePerks[i].Sum(PerkPower) + Draft.PackageCards[i].Sum(CardPower), SimWin = Draft.PackageWin[i] };
                    o.Perks.AddRange(Draft.PackagePerks[i]); o.Cards.AddRange(Draft.PackageCards[i]);
                    return o;
                }).ToList();
            var teams = Sample(Teams.All.ToList(), Draft.Offers, rng);
            return teams.Select(t => DealOne(t, t.Element, Draft.Perks, Draft.Cards, Draft.Target, rng)).ToList();
        }

        // an opponent's draft: perks only (opponents have no stat cards), aimed at the perk part of a package
        public static List<string> OpponentPerks(int seed, int slot, string element)
        {
            var cards = Draft.CardPower.Where(p => p > Draft.MinPower).OrderBy(p => p).ToList();
            float cardMedian = cards.Count > 0 ? cards[cards.Count / 2] : 0f;
            float target = Mathf.Max(0f, Draft.Target - Draft.Cards * cardMedian) * Draft.OpponentPerks / Mathf.Max(1, Draft.Perks);
            var rng = new System.Random(seed * 31 + slot * 7919);
            return DealOne(null, element, Draft.OpponentPerks, 0, target, rng)?.Perks ?? new List<string>();
        }

        public static string SeedKey(int seed) => "draft-" + seed;
        public static int SeedOf(string key) => key != null && key.StartsWith("draft-") && int.TryParse(key.Substring(6), out var s) ? s : 0;

        // kick-off of a draft match: the opponent gets its drafted perks on top of its own
        public static void GiveOpponent(GameObject team, int slot)
        {
            if (RunState.Draft == null || team == null) return;
            var stats = team.GetComponent<PlayerStats>();
            if (stats == null) return;
            string element = TeamRoster.Current != null ? TeamRoster.Current.Element : Teams.VanillaElement(team.name.Replace("(Clone)", "").Trim());
            var given = OpponentPerks(SeedOf(RunState.Daily), slot, element).Select(PerkRegistry.Get).Where(p => p != null && !stats.techniques.Contains(p)).Cast<Technique>().ToList();
            stats.techniques.AddRange(given);
            OpponentScaling.LastGiven = given.ToList();
            var names = given.Select(t => ((DataPerk)t).Def.Title).ToList();
            if (names.Count > 0) TeamRoster.Announce(team, "DRAFTED: " + string.Join(", ", names));
            Plugin.Log.LogInfo($"Draft: opponent in slot {slot} drafted {string.Join(", ", names)}");
        }

        public static TeamDef TeamOf(string id) => Teams.All.FirstOrDefault(t => t.Id == id);
    }
}

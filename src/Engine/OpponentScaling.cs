using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Generated;
using HarmonyLib;
using UnityEngine;

namespace HangtimeOvertime.Engine
{
    // Infinite/Loop opponent strength from the scaling sheet (sheets/scaling.json, the one place to tune it;
    // tools/simlib.py implements the same formulas). Round is the main driver; the player's power nudges it.
    internal static class OpponentScaling
    {
        public static int Round =>
            RunState.Mode == RunMode.Infinite ? RunState.InfiniteMatch :
            RunState.Mode == RunMode.Loop ? 8 * (RunState.Loop - 1) + GameManager.gameNumber : 0;

        public static List<Technique> ResumePerks;   // a resumed run: the perks this opponent had when it was saved, used once
        public static List<Technique> LastGiven = new List<Technique>();

        public static string ClassOf(int slot) => slot >= 1 && slot <= 8 ? Generated.Teams.SlotClass[slot] : "regular";

        // perks + Overtime card points / 2 + the game's own stat levels
        public static float PlayerPower()
        {
            var ps = GameManager.Instance.playerStats;
            return ps.techniques.Count + StatCards.Points.Values.Sum() / 2f + ps.statUpgrades.Sum(s => s.currentLevel);
        }

        public static float Points(int round, string cls, float power, float offset = 0f)
        {
            float pts = Scaling.PointsBase + Scaling.PointsPerRound * (round - 1) + offset;
            if (cls == "boss") pts += Scaling.PointsBossBonus;
            else if (cls == "combo") pts += Scaling.PointsComboBonus;
            float expected = Scaling.ExpectedPowerBase + Scaling.ExpectedPowerPerRound * (round - 1);
            float ratio = Mathf.Clamp((power + 1f) / (expected + 1f), Scaling.PlayerPowerRatioMin, Scaling.PlayerPowerRatioMax);
            pts *= 1f + Scaling.PlayerPowerWeight * (ratio - 1f);
            return Mathf.Max(0f, pts);
        }

        public static float StatMult(int round)
        {
            float ramp = Scaling.BaseRampRounds > 0f ? Mathf.Clamp01((round - 1) / Scaling.BaseRampRounds) : 1f;
            float early = Scaling.BaseStatMult + (1f - Scaling.BaseStatMult) * ramp;
            return early * Mathf.Min(Scaling.OverMax, 1f + Scaling.OverPerRound * Mathf.Max(0f, round - Scaling.OverStart));
        }

        public static float MoveMult(int round)
        {
            float ramp = Scaling.BaseRampRounds > 0f ? Mathf.Clamp01((round - 1) / Scaling.BaseRampRounds) : 1f;
            float early = Scaling.BaseMoveMult + (1f - Scaling.BaseMoveMult) * ramp;
            return early * Mathf.Min(Scaling.MoveMax, 1f + Scaling.MovePerRound * Mathf.Max(0f, round - 1));
        }

        public static int PerkCount(int round, string cls)
        {
            float n = Scaling.PerksBase + Scaling.PerksPerRound * (round - 1) + (cls == "boss" ? Scaling.PerksBossBonus : 0f);
            return Mathf.Clamp(Mathf.FloorToInt(n + 1e-4f), 0, (int)Scaling.PerksMax);
        }

        // Re-levels a freshly spawned opponent and sets its perks for this round.
        public static void Apply(GameObject team, TeamDef def, int slot)
        {
            int round = Mathf.Max(1, Round + Plugin.InfiniteRoundOffset.Value);   // config: [Infinite] Difficulty
            string cls = ClassOf(slot);
            var ot = team.GetComponent<OpponentTeam>();
            var stats = team.GetComponent<PlayerStats>();
            if (ot == null || stats == null) return;
            float power = PlayerPower();

            // stats: the game's own weighted level-ups with this round's points, then the curves
            foreach (var s in stats.statUpgrades) s.currentLevel = 0;
            var overrideWeights = def == null ? Generated.Teams.VanillaWeights(team.name.Replace("(Clone)", "").Trim()) : null;
            if (overrideWeights != null) Traverse.Create(ot).Field("statWeights").SetValue(overrideWeights);
            string baseName = team.name.Replace("(Clone)", "").Trim();
            float offset = def != null ? def.PointsOffset : Generated.Teams.VanillaPointsOffset(baseName);
            float points = Points(round, cls, power, offset);
            ot.Init(points);
            float sm = StatMult(round), mm = MoveMult(round);
            foreach (var s in stats.statUpgrades)
                for (int i = 0; i < s.levels.Length; i++) s.levels[i] *= sm;
            foreach (var pc in team.GetComponentsInChildren<PlayerController>(true)) pc.moveSpeed *= mm;

            // perks: the team's own (new-team signatures / vanilla built-ins) first, then themed random draws
            var builtIn = stats.techniques.ToList();
            stats.techniques.Clear();
            foreach (var ab in team.GetComponentsInChildren<AbilityBooleans>(true))
            {
                ab.canSpikeFreeBalls = ab.blockBoost = ab.perfectBump = ab.absoluteBlock = false;
                ab.skyServe = ab.riskySet = ab.hybridServe = false;   // the given techniques set their own again
            }
            var allowed = new HashSet<string> { "common" };
            if (round >= Scaling.RareFrom) allowed.Add("rare");
            if (round >= Scaling.EpicFrom) allowed.Add("epic");
            string Rarity(Technique t) => t is DataPerk d ? d.Def.Rarity : Scaling.VanillaRarity(t.GetType().Name);
            var own = def != null ? def.Signature.Select(PerkRegistry.Get).Where(p => p != null).Cast<Technique>().ToList() : builtIn;
            int count = PerkCount(round, cls);
            var given = own.Where(t => allowed.Contains(Rarity(t))).Take(count).ToList();
            string element = def != null ? def.Element : Generated.Teams.VanillaElement(team.name.Replace("(Clone)", "").Trim());
            var pool = PerkRegistry.All.Where(p => p.Def.OpponentOk && allowed.Contains(p.Def.Rarity) && !given.Contains(p))
                .Select(p => (tech: (Technique)p, el: p.Def.Element))
                .Concat(Scaling.OpponentTechniques.Select(PerkRegistry.FindVanilla).Where(t => t != null && !given.Contains(t) && allowed.Contains(Rarity(t))).Select(t => (tech: t, el: "none")))
                .ToList();
            if (ResumePerks != null) { given = ResumePerks; pool.Clear(); ResumePerks = null; }
            while (given.Count < count && pool.Count > 0)
            {
                float total = pool.Sum(p => p.el == element && element != "none" ? Scaling.ThemeWeight : 1f);
                float x = Random.value * total, acc = 0f;
                int pick = pool.Count - 1;
                for (int i = 0; i < pool.Count; i++)
                {
                    acc += pool[i].el == element && element != "none" ? Scaling.ThemeWeight : 1f;
                    if (x <= acc) { pick = i; break; }
                }
                given.Add(pool[pick].tech);
                pool.RemoveAt(pick);
            }
            stats.techniques.AddRange(given);
            LastGiven = given.ToList();
            Plugin.Log.LogInfo($"Scaling round {round} ({cls}): {points:F1} points, stats x{sm:F2}, move x{mm:F2}, player power {power:F1}, " +
                               $"perks [{string.Join(", ", given.Select(t => t is DataPerk d ? d.Def.Title : PerkRegistry.VanillaTitle(t)))}]");
            // the speech bubble stays readable: four names at most, then "+N more"
            var names = given.Select(t => t is DataPerk d ? d.Def.Title : PerkRegistry.VanillaTitle(t)).ToList();
            if (names.Count > 0) TeamRoster.Announce(team, string.Join(", ", names.Take(4)) + (names.Count > 4 ? $" +{names.Count - 4} more" : ""));
        }
    }
}

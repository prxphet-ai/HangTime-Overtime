using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Generated;
using UnityEngine;

namespace HangtimeOvertime.Engine
{
    // Overtime stat cards: they ride on the game's own stat-card buttons (UpgradePair) but add
    // card points to the run instead of game levels, so they can carry drawbacks and mod stats.
    internal static class StatCards
    {
        private const string Marker = "overtime:";
        private static readonly Dictionary<string, StatCardDef> byId = Generated.StatCards.All.ToDictionary(c => c.Id);
        private static readonly Dictionary<string, StatDef> stats = Generated.Stats.All.ToDictionary(s => s.Id);

        // card points per stat this run (player team), and the right team's in Versus
        public static readonly Dictionary<string, float> Points = new Dictionary<string, float>();
        public static readonly Dictionary<string, float> Side1Points = new Dictionary<string, float>();
        public static Dictionary<string, float> PointsFor(int side) => side == 0 ? Points : Side1Points;

        public static void ResetRun() { Points.Clear(); Side1Points.Clear(); }

        public static StatCardDef CardOf(UpgradePair pair) =>
            pair?.upgrades != null && pair.upgrades.Count == 1 && pair.upgrades[0].StartsWith(Marker) && byId.TryGetValue(pair.upgrades[0].Substring(Marker.Length), out var c) ? c : null;

        public static UpgradePair MakePair(StatCardDef c)
        {
            var words = new List<string>();
            for (int i = 0; i < c.Stats.Length; i++)
            {
                float v = c.Points[i];
                string sign = v >= 2.5f ? "++" : v > 0f ? "+" : v <= -1.5f ? "--" : "-";
                words.Add(sign + stats[c.Stats[i]].Label);
            }
            return new UpgradePair { title = c.Title, description = string.Join(" ", words), upgrades = new List<string> { Marker + c.Id } };
        }

        public static void AddToPool(List<UpgradePair> pool)
        {
            pool.RemoveAll(p => CardOf(p) != null);
            bool onlyOvertime = Debugging.DevKeys.OnlyOvertimeCards;   // development (F11): test the Overtime cards
            if (onlyOvertime) pool.Clear();
            foreach (var c in Generated.StatCards.All)
            {
                float odds = c.Rarity == "epic" ? Generated.StatCards.OddsEpic : c.Rarity == "rare" ? Generated.StatCards.OddsRare : Generated.StatCards.OddsCommon;
                if (onlyOvertime || Random.value < odds) pool.Add(MakePair(c));
            }
            for (int i = pool.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
            pool.Capacity = pool.Count;   // the game wraps its index with Capacity
        }

        public static string LongText(StatCardDef c)
        {
            var lines = new List<string> { c.Blurb, "" };
            for (int i = 0; i < c.Stats.Length; i++)
                lines.Add($"{stats[c.Stats[i]].Label} {(c.Points[i] > 0 ? "+" : "")}{c.Points[i]:0.##}");
            lines.Add("");
            lines.Add(c.Rarity.ToUpperInvariant());
            return string.Join("\n", lines);
        }

        public static Color RarityColor(string rarity) =>
            rarity == "epic" ? new Color(1f, 0.78f, 0.29f) : rarity == "rare" ? new Color(0.5f, 0.85f, 1f) : Color.white;

        public static StatCardDef Get(string id) => byId.TryGetValue(id, out var c) ? c : null;
        public static IEnumerable<StatCardDef> All => byId.Values;
        public static string Label(string statId) => stats.TryGetValue(statId, out var s) ? s.Label : statId;

        public static void Take(StatCardDef c, int side = 0)
        {
            var points = PointsFor(side);
            for (int i = 0; i < c.Stats.Length; i++)
            {
                var s = stats[c.Stats[i]];
                points.TryGetValue(s.Id, out var have);
                points[s.Id] = Mathf.Clamp(have + c.Points[i], s.MinSteps, s.MaxSteps);
            }
            Plugin.Log.LogInfo($"Stat card '{c.Title}' (side {side}): " + string.Join(", ", points.Select(kv => $"{kv.Key} {kv.Value:0.##}")));
        }

        // Turn the card points of a player's team into game units for that player.
        public static void ApplyTo(ControllerFx p)
        {
            var points = PointsFor(p.Side);
            foreach (var kv in points)
            {
                if (!stats.TryGetValue(kv.Key, out var s) || kv.Value == 0f) continue;
                foreach (var field in s.Fields)
                {
                    switch (field)
                    {
                        case "recovery": if (!p.IsSetter) p.RecoverySteps = kv.Value; break;
                        case "setter_move": if (p.IsSetter) p.AddBase("setter_move", kv.Value * s.Step); break;
                        case "move": if (!p.IsSetter) p.AddBase("move", kv.Value * s.Step); break;
                        case "SpinServe": if (!p.IsSetter) p.AddBase(field, kv.Value * Generated.Stats.ServeSpin); break;
                        case "ServeJump": if (!p.IsSetter) p.AddBase(field, kv.Value * Generated.Stats.ServeJump); break;
                        case "FloatServe": if (!p.IsSetter) p.AddBase(field, kv.Value * Generated.Stats.ServeFloat); break;
                        default: if (!p.IsSetter) p.AddBase(field, kv.Value * s.Step); break;
                    }
                }
            }
            if (points.Count > 0) Plugin.Trace($"{p.name}: stat cards applied ({string.Join(", ", points.Select(kv => $"{kv.Key} {kv.Value:0.##}"))})");
        }
    }
}

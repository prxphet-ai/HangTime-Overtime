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

        // card points per stat this run (player team)
        public static readonly Dictionary<string, float> Points = new Dictionary<string, float>();

        public static void ResetRun() => Points.Clear();

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
            foreach (var c in Generated.StatCards.All)
            {
                float odds = c.Rarity == "epic" ? Generated.StatCards.OddsEpic : c.Rarity == "rare" ? Generated.StatCards.OddsRare : Generated.StatCards.OddsCommon;
                if (Random.value < odds) pool.Add(MakePair(c));
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

        public static void Take(StatCardDef c)
        {
            for (int i = 0; i < c.Stats.Length; i++)
            {
                var s = stats[c.Stats[i]];
                Points.TryGetValue(s.Id, out var have);
                Points[s.Id] = Mathf.Clamp(have + c.Points[i], s.MinSteps, s.MaxSteps);
            }
            Plugin.Log.LogInfo($"Stat card '{c.Title}': " + string.Join(", ", Points.Select(kv => $"{kv.Key} {kv.Value:0.##}")));
        }

        // Turn the run's card points into game units for one player of the player's team.
        public static void ApplyTo(ControllerFx p)
        {
            foreach (var kv in Points)
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
            if (Points.Count > 0) Plugin.Trace($"{p.name}: stat cards applied ({string.Join(", ", Points.Select(kv => $"{kv.Key} {kv.Value:0.##}"))})");
        }
    }
}

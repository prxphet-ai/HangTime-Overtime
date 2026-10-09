using System.Collections.Generic;
using System.Linq;
using System.Text;
using HangtimeOvertime.Core;
using HarmonyLib;
using UnityEngine;

namespace HangtimeOvertime.Debugging
{
    // Development aid (DumpScenes): logs the game data the sheets depend on.
    internal static class Dumps
    {
        public static void Upgrade(UpgradeManager um, List<Logo_technique> cards, int limitBreakLevel)
        {
            var sb = new StringBuilder($"Upgrade screen: limitBreakLevel={limitBreakLevel} gameNumber={GameManager.gameNumber}\n");
            foreach (var c in cards)
                sb.AppendLine($"  card '{c.title}' technique={PerkRegistry.TechniqueOf(c)?.GetType().Name} sprite={(c.sprite ? c.sprite.name : "-")} text='{c.description}'");
            var pairs = Traverse.Create(um).Field("upgrades").GetValue<List<UpgradePair>>();
            if (pairs != null)
                foreach (var p in pairs) sb.AppendLine($"  stat card '{p.title}': {string.Join(",", p.upgrades)}");
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static bool gameDataDumped;

        // Team prefabs, line-ups and the player's stat tables, read from GameManager (no match needed).
        public static void GameData()
        {
            var gm = GameManager.Instance;
            if (gameDataDumped || gm == null) return;
            gameDataDumped = true;
            var sb = new StringBuilder("Game data:\n");
            void Team(string label, GameObject t)
            {
                if (t == null) { sb.AppendLine($"  {label}: null"); return; }
                var ot = t.GetComponent<OpponentTeam>();
                var weights = ot == null ? "" : string.Join(",", Traverse.Create(ot).Field("statWeights").GetValue<List<StatWeight>>().Select(w => $"{w.statName}:{w.weight}"));
                sb.AppendLine($"  {label}: {t.name} team='{ot?.teamName}' banner={(ot?.banner ? ot.banner.name : "-")} pitch={ot?.talkPitch} weights=[{weights}] intro='{ot?.introDialogue}'");
            }
            foreach (var t in gm.teams) Team("team", t);
            foreach (var t in gm.comboTeams) Team("combo", t);
            Team("practice", gm.practiceTeam); Team("midBoss1", gm.midBoss1); Team("midBoss2", gm.midBoss2); Team("final", gm.finalBoss);
            var lineUps = Traverse.Create(gm.tournamentLineUps).Field("lineUps").GetValue<List<LineUp>>();
            if (lineUps != null)
                foreach (var l in lineUps) sb.AppendLine("  lineup: " + string.Join(" | ", l.lineUp.Select(x => x ? x.name : "-")));
            foreach (var s in gm.playerStats.statUpgrades)
                sb.AppendLine($"  player {s.statName} levels [{string.Join(", ", s.levels.Select(v => v.ToString("F2")))}]");
            if (gm.teams.Count > 0) Hierarchy(gm.teams[0].transform, 0, sb);
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static void Hierarchy(Transform t, int depth, StringBuilder sb)
        {
            if (depth > 5) return;
            sb.Append(' ', 2 + depth * 2).Append(t.name).Append(" @").Append(t.localPosition.ToString("F1")).Append(" :");
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                sb.Append(' ').Append(c.GetType().Name);
                if (c is SpriteRenderer sr) sb.Append($"[{(sr.sprite ? sr.sprite.name : "-")} {sr.color} order {sr.sortingOrder} layer {sr.sortingLayerName}]");
            }
            sb.AppendLine();
            for (int i = 0; i < t.childCount; i++) Hierarchy(t.GetChild(i), depth + 1, sb);
        }

        public static void Opponent(GameObject team)
        {
            var sb = new StringBuilder($"Opponent {team.name}:\n");
            var stats = team.GetComponent<PlayerStats>();
            if (stats != null)
                foreach (var s in stats.statUpgrades)
                    sb.AppendLine($"  {s.statName} lvl {s.currentLevel} = {s.GetCurrentValue():F2}  levels [{string.Join(", ", s.levels.Select(v => v.ToString("F1")))}]");
            foreach (var pc in team.GetComponentsInChildren<PlayerController>(true))
                sb.AppendLine($"  {pc.name} moveSpeed {pc.moveSpeed:F2}");
            var mine = GameManager.Instance.playerStats;
            foreach (var s in mine.statUpgrades)
                sb.AppendLine($"  (player) {s.statName} lvl {s.currentLevel} = {s.GetCurrentValue():F2}");
            Plugin.Log.LogInfo(sb.ToString());
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HarmonyLib;
using UnityEngine;

namespace HangtimeOvertime.Engine
{
    // Individual picks (co-op, Versus sides with two players): a perk a player picked is theirs. Its triggers that are a
    // character's own action (spike, serve, tip, set, dig, block, jump, block jump) only fire for that player; the rest
    // (passives, streaks, the other team's spikes...) work for the whole team, as do stat cards and the game's own
    // techniques: those are labelled TEAM-WIDE. A side with one human keeps the old behaviour (owner 0 = the team).
    internal static class Ownership
    {
        private static readonly Dictionary<Technique, int>[] owners = { new Dictionary<Technique, int>(), new Dictionary<Technique, int>() };
        public static readonly HashSet<string> PersonalTriggers = new HashSet<string> { "spike", "serve", "tip", "set", "dig", "block", "jump", "block_jump" };

        public static void Clear() { owners[0].Clear(); owners[1].Clear(); }

        public static void Set(int side, Technique t, int number) { if (t != null && number > 0) owners[side][t] = number; }
        public static int Of(int side, Technique t) => t != null && owners[side].TryGetValue(t, out var n) ? n : 0;

        // a perk that can belong to one player (it has at least one trigger that is a character's own action)
        public static bool Personal(Technique t) => t is DataPerk d && d.Def.Triggers.Any(PersonalTriggers.Contains);
        public static string Label(Technique t) => Personal(t) ? "" : "TEAM-WIDE";

        // the player number controlling a character (Versus slot number, co-op 1 / 2), 0 for an AI or a solo game
        public static int NumberOf(PlayerController pc)
        {
            if (pc == null) return 0;
            if (VersusState.Active && Patches.VersusDriver.Current != null)
                foreach (var kv in Patches.VersusDriver.Current.Players)
                    if (kv.Value == pc) return kv.Key.Control == SlotControl.Human ? kv.Key.Number : 0;
            if (Coop)
            {
                var lim = Object.FindFirstObjectByType<LocalInputManager>();
                if (lim != null) return lim.player1 == pc ? 1 : lim.player2 == pc ? 2 : 0;
            }
            return 0;
        }

        public static bool Coop => !VersusState.Active && GameManager.Instance != null && GameManager.Instance.numberOfPlayers == 2 && !TitleController.onTitle;

        // does this perk fire for this trigger and character
        public static bool Fires(DataPerk perk, string trigger, PlayerController pc, int side)
        {
            int owner = Of(side, perk);
            if (owner == 0 || pc == null || !PersonalTriggers.Contains(trigger) || Engine.SideOf(pc) != side) return true;
            int n = NumberOf(pc);
            if (Plugin.DevKeys.Value) Plugin.Log.LogInfo($"[own] {perk.Def.Title} on {trigger}: P{owner}'s, touched by P{n} -> {(n == owner ? "fires" : "skipped")}");
            return n == owner;
        }

        // the build of one side, per player: "P1: A, B", "P2: C", "TEAM: D, stat cards"
        public static List<string> Lines(int side, IEnumerable<Technique> perks, System.Func<Technique, string> title)
        {
            var byOwner = perks.GroupBy(t => Of(side, t)).OrderBy(g => g.Key == 0 ? 99 : g.Key);
            var lines = new List<string>();
            foreach (var g in byOwner)
                lines.Add((g.Key == 0 ? "TEAM" : "P" + g.Key) + ": " + string.Join(", ", g.Select(title)));
            return lines;
        }
    }
}

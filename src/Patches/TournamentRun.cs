using System;
using System.Collections;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HarmonyLib;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // Playing a community tournament: the game's own run (Normal mode) from slot 9 - N to the final, every opponent a
    // custom team (TeamRoster.Choose / Dress), set up here at kick-off with exactly its stat levels, perks and stat cards.
    // FIXED: the player starts with the creator's levels, perks and cards and the upgrade screen only shows the bracket;
    // DRAFT: the player starts with nothing and picks as usual. The run never touches Classic's saves, records or boards
    // (its own run slot, record and, once online, its own fastest-clear board) and unlocks no Steam achievements.
    internal static class TournamentRun
    {
        public static bool Active => Tournaments.Current != null && RunState.Mode == RunMode.Normal && !VersusState.Active;

        // the title's CLASSIC button, after the tournament menu set Tournaments.Pending (MenuPatches.TitleMenu)
        public static void Start()
        {
            var e = Tournaments.Pending;
            bool test = Tournaments.PendingTest;
            Tournaments.Pending = null; Tournaments.PendingTest = false;
            var t = e != null ? Tournaments.Content(e) : null;
            if (t == null) { Tournaments.Current = null; return; }
            Tournaments.Current = new Tournaments.Play { Entry = e, T = t, Test = test };
            RunSaves.ClearCommunity();
            Begin();
            Plugin.Log.LogInfo($"Community tournament started: {t.name} ({t.mode}, {t.teams.Length} matches, difficulty {Tournaments.Difficulty(t)}{(test ? ", test play" : "")})");
        }

        // a fresh attempt (the start, or RETRY after a loss)
        private static void Begin()
        {
            var p = Tournaments.Current;
            RunState.Mode = RunMode.Normal;
            RunState.Loop = 1;
            RunState.Daily = null; RunState.Draft = null; RunState.DailyCounts = false;
            Core.Daily.Pending = null; Core.Daily.PendingCup = false;
            var gm = GameManager.Instance;
            gm.playerStats.techniques.Clear();
            StatCards.ResetRun();
            LimitBreakEffects.ResetLimits();
            GameManager.statOverload = false;
            StatTracker.runTimer = 0f;
            TutorialManager.onTutorial = false;
            Ownership.Clear();
            foreach (var stat in gm.playerStats.statUpgrades)
            {
                int i = Array.IndexOf(Tournaments.Stats, stat.statName);
                stat.currentLevel = p.T.Fixed && i >= 0 ? Mathf.Clamp(p.T.player.levels[i], 0, 3) : 0;
            }
            if (p.T.Fixed)
            {
                foreach (var id in p.T.player.perks) { var perk = PerkRegistry.Get(id); if (perk != null) gm.playerStats.techniques.Add(perk); }
                foreach (var id in p.T.player.cards) { var c = StatCards.Get(id); if (c != null) StatCards.Take(c); }
            }
            GameManager.gameNumber = p.FirstSlot;
            p.Entry.attempts++;
            Tournaments.Save();
            RunClock.Start();
        }

        // RETRY on the lose screen (MenuPatches.Restart): the same tournament again from its first match
        public static void Retry() { if (Active) { Begin(); Plugin.Log.LogInfo("Community tournament: retry"); } }

        // kick-off (RunPatches.MatchStart): the custom team's exact build
        public static void SetupOpponent(GameObject team, int slot)
        {
            var p = Tournaments.Current;
            var tm = p?.TeamAt(slot);
            var stats = team != null ? team.GetComponent<PlayerStats>() : null;
            if (tm == null || stats == null) return;
            foreach (var s in stats.statUpgrades)
            {
                int i = Array.IndexOf(Tournaments.Stats, s.statName);
                s.currentLevel = i >= 0 ? Mathf.Clamp(tm.levels[i], 0, Mathf.Min(3, s.levels.Length - 1)) : 0;
            }
            stats.techniques.Clear();
            foreach (var ab in team.GetComponentsInChildren<AbilityBooleans>(true))
            {
                ab.canSpikeFreeBalls = ab.blockBoost = ab.perfectBump = ab.absoluteBlock = false;
                ab.skyServe = ab.riskySet = ab.hybridServe = false;
            }
            var given = tm.perks.Select(PerkRegistry.Get).Where(x => x != null).Cast<Technique>().ToList();
            stats.techniques.AddRange(given);
            OpponentScaling.LastGiven = given.ToList();
            StatCards.Side1Points.Clear();
            foreach (var id in tm.cards) { var c = StatCards.Get(id); if (c != null) StatCards.Take(c, 1); }
            var names = given.Select(x => ((DataPerk)x).Def.Title).ToList();
            if (names.Count > 0) TeamRoster.Announce(team, string.Join(", ", names.Take(4)) + (names.Count > 4 ? $" +{names.Count - 4} more" : ""));
            Plugin.Log.LogInfo($"Community match {p.IndexOf(slot) + 1}/{p.Matches}: {tm.name} on {tm.@base}, levels [{string.Join(",", tm.levels)}], perks [{string.Join(", ", tm.perks)}], cards [{string.Join(", ", tm.cards)}]");
        }

        // the run log numbers a tournament's matches 1..N (the server's clear check reads them that way)
        public static int LogSlot(int slot) => Active ? Tournaments.Current.IndexOf(slot) + 1 : slot;

        // the final was won (StreakPatches, before the game's own win flow)
        public static void Cleared()
        {
            var p = Tournaments.Current;
            RunClock.Stop();
            float secs = RunClock.Seconds;
            var e = p.Entry;
            bool best = e.best <= 0f || secs < e.best;
            e.clears++;
            if (best) e.best = secs;
            string line = $"CLEARED {p.T.name.ToUpperInvariant()} IN {RunClock.Format(secs)}" + (best && e.clears > 1 ? " · NEW BEST" : "");
            // the creator's own clear of this exact content: the record that lets it be published
            if (e.mine && !RunClock.Tainted && !Tournaments.Verified(e))
            {
                e.verify = Tournaments.EncodeVerify(new TourVerify { hash = e.Hash, log = RunClock.LogText, seconds = secs, mod = Plugin.Version });
                line += " · READY TO PUBLISH";
            }
            else if (e.mine && RunClock.Tainted) line += $" · NOT A CLEAR RECORD ({RunClock.TaintReason})";
            Tournaments.Save();
            RunSaves.ClearCommunity();
            Toast.Show(line);
            Online.TournamentNet.Result(p, true, secs, RunClock.LogText);
            Plugin.Log.LogInfo($"Community tournament cleared: {p.T.name} in {secs:F1}s (clears {e.clears}, attempts {e.attempts}, best {e.best:F1}s); log {RunClock.LogText}");
        }

        public static void Lost()
        {
            var p = Tournaments.Current;
            RunClock.Stop();
            RunSaves.ClearCommunity();
            Online.TournamentNet.Result(p, false, RunClock.Seconds, RunClock.LogText);
            Plugin.Log.LogInfo($"Community tournament lost at match {p.IndexOf(GameManager.gameNumber) + 1}/{p.Matches}: {p.T.name}");
        }

        // ------------------------------------------------------------ the bracket: one column per match

        public static BracketModel Bracket(int won)
        {
            var p = Tournaments.Current;
            var m = new BracketModel { Champion = won >= 8 };
            string tag = p.Test ? "TEST PLAY · " : "";
            m.Title = m.Champion ? "CHAMPIONS!" : $"{tag}{p.T.name.ToUpperInvariant()}";
            for (int i = 0; i < p.Matches; i++)
            {
                var tm = p.T.teams[i];
                var r = new BracketRound { Name = i == p.Matches - 1 ? "FINAL" : $"MATCH {i + 1}" };
                r.Matches.Add(new BracketMatch { A = m.You, B = tm.name, Winner = p.FirstSlot + i <= won ? 0 : -1, Note = $"{tm.perks.Length} PERKS · {tm.cards.Length} CARDS", EmblemB = EmblemArt.Make(tm.emblem, tm.name) });
                m.Rounds.Add(r);
            }
            int done = won - p.FirstSlot + 1;
            if (!m.Champion)
            {
                m.FromRound = done - 1; m.ToRound = done;
                var next = p.T.teams[done];
                m.Sub = $"NEXT: VS {next.name.ToUpperInvariant()}" + (next.perks.Length > 0 ? "  ·  " + string.Join(", ", next.perks.Select(id => PerkRegistry.Get(id)?.Def.Title ?? id).Take(3)).ToUpperInvariant() + (next.perks.Length > 3 ? " ..." : "") : "");
            }
            else m.Sub = $"{p.T.name.ToUpperInvariant()}: {(p.Matches == 1 ? "THE MATCH" : $"ALL {p.Matches} MATCHES")} WON IN {RunClock.Format(RunClock.Seconds)}";
            return m;
        }

        // ------------------------------------------------------------ patches

        [HarmonyPatch(typeof(UpgradeManager), "Start")]
        private static class BetweenMatches
        {
            private static int won;

            [HarmonyPrefix]
            private static void Prefix() => won = GameManager.gameNumber;

            [HarmonyPostfix]
            private static void Postfix(UpgradeManager __instance)
            {
                if (!Active || won < Tournaments.Current.FirstSlot || won >= 8) return;
                bool fix = Tournaments.Current.T.Fixed;
                if (fix)
                {
                    // no picks: the screen's buttons go and the next match loads once the bracket is closed
                    foreach (var f in new[] { "nextButton", "skipButton" })
                    {
                        var v = Traverse.Create(__instance).Field(f).GetValue<object>();
                        var go = v is GameObject g ? g : (v as Component)?.gameObject;
                        if (go != null) go.SetActive(false);
                    }
                }
                int slot = won;
                var um = __instance;
                um.StartCoroutine(After(0.35f, () => BracketPanel.Show(Bracket(slot), true, fix ? () => { if (um != null) NextMatch(um); } : (Action)null)));
            }
        }

        private static void NextMatch(UpgradeManager um)
        {
            var load = AccessTools.Method(typeof(UpgradeManager), "LoadGame");
            um.StartCoroutine((IEnumerator)load.Invoke(um, null));     // the game's own: waits, gameNumber + 1, loads the match (MenuPatches saves the run)
        }

        // FIXED: the pad's START skip would load the next match a second time
        [HarmonyPatch(typeof(UpgradeManager), "SkipUpgrade")]
        private static class NoSkip
        {
            [HarmonyPrefix]
            private static bool Prefix() => !(Active && Tournaments.Current.T.Fixed);
        }

        [HarmonyPatch(typeof(WinManager), "Start")]
        private static class Champions
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (Active) Runner.Instance.StartCoroutine(After(1.2f, () => BracketPanel.Show(Bracket(8), false)));
            }
        }

        // a custom tournament's final is the game's final: no Steam achievements for it
        [HarmonyPatch(typeof(AchievementManager), "Unlock")]
        private static class NoAchievements
        {
            [HarmonyPrefix]
            private static bool Prefix() => !Active;
        }

        private static IEnumerator After(float seconds, Action a)
        {
            yield return new WaitForSecondsRealtime(seconds);
            a();
        }
    }
}

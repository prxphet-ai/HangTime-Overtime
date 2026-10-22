using System;
using System.IO;
using HangtimeOvertime.Generated;
using UnityEngine;

namespace HangtimeOvertime.Core
{
    public enum RunMode { Normal, Infinite, Loop, Versus }

    // Which mode the run is in, its counters, per-match perk state, and the mod's own save file.
    internal static class RunState
    {
        public static RunMode Mode = RunMode.Normal;

        public static int InfiniteMatch = 1;   // 1-based match number in an infinite run
        public static int InfiniteWins;
        public static int Loop = 1;            // 1 = the normal tournament
        public static string Daily;            // an Infinite run that is that UTC day's daily run (null: a normal one)
        public static bool DailyCounts;        // it's that day's first run
        public static string Draft;            // a draft cup: the drafted team's id (the bracket is seeded by Daily = "draft-<seed>")

        public static OvertimeSave Save = new OvertimeSave();
        private static string SavePath => Path.Combine(Application.persistentDataPath, "HangtimeOvertime.json");

        public static ModeDef ModeDef =>
            Mode == RunMode.Infinite ? Modes.Infinite : Mode == RunMode.Loop ? Modes.Loop : Mode == RunMode.Versus ? Modes.Versus : Modes.Normal;

        // How many times the bracket has been cleared this run (drives the scaling sheet).
        public static int Tier =>
            Mode == RunMode.Infinite ? (InfiniteMatch - 1) / 8 :
            Mode == RunMode.Loop ? Loop - 1 : 0;

        public static int InfiniteSlot => (InfiniteMatch - 1) % 8 + 1;

        public static void LoadSave()
        {
            try
            {
                if (File.Exists(SavePath)) Save = JsonUtility.FromJson<OvertimeSave>(File.ReadAllText(SavePath)) ?? new OvertimeSave();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read " + SavePath + ": " + e.Message);
                Save = new OvertimeSave();
            }
        }

        public static void WriteSave()
        {
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(Save, true)); }
            catch (Exception e) { Plugin.Log.LogWarning("Could not write " + SavePath + ": " + e.Message); }
        }

        // A fresh run, as the game's own practice match would do it, then straight into match 1.
        public static void StartInfinite()
        {
            // the DAILY menu starts the day's run; trying again after a daily run is another run of today's
            if (Core.Daily.Pending != null && !Core.Daily.PendingCup) { Daily = Core.Daily.Pending; Core.Daily.Pending = null; }
            else if (Daily != null) Daily = Core.Daily.Today;
            if (Daily != null) DailyCounts = Core.Daily.Begin(Core.Daily.Key(Daily, false));
            Mode = RunMode.Infinite;
            InfiniteMatch = 1;
            InfiniteWins = 0;
            Loop = 1;
            var gm = GameManager.Instance;
            foreach (var stat in gm.playerStats.statUpgrades) stat.currentLevel = 0;
            gm.playerStats.techniques.Clear();
            Engine.StatCards.ResetRun();
            LimitBreakEffects.ResetLimits();
            GameManager.statOverload = false;
            StatTracker.runTimer = 0f;
            TutorialManager.onTutorial = false;
            if (gm.tournamentLineUps != null) gm.tournamentLineUps.NewLineUp();
            GameManager.gameNumber = InfiniteSlot;
            if (Daily == null && gm.numberOfPlayers == 1) Save.infiniteRuns++;
            Engine.Ownership.Clear();
            WriteSave();
            Plugin.Log.LogInfo(Daily != null ? "Infinite run started (daily " + Daily + ")" : "Infinite run started");
        }

        // the daily cup: a Classic-style bracket from match 1 (no practice match), opponents and offers seeded by the day
        public static void StartCup()
        {
            Daily = Core.Daily.Pending ?? Core.Daily.Today;
            Core.Daily.Pending = null; Core.Daily.PendingCup = false;
            Mode = RunMode.Normal;
            Loop = 1;
            var gm = GameManager.Instance;
            foreach (var stat in gm.playerStats.statUpgrades) stat.currentLevel = 0;
            gm.playerStats.techniques.Clear();
            Engine.StatCards.ResetRun();
            LimitBreakEffects.ResetLimits();
            GameManager.statOverload = false;
            StatTracker.runTimer = 0f;
            TutorialManager.onTutorial = false;
            GameManager.gameNumber = 1;
            Draft = null;
            DailyCounts = Core.Daily.Begin(Core.Daily.Key(Daily, true));
            Plugin.Log.LogInfo("Daily cup started (" + Daily + ")");
        }

        // the draft cup: the cup's machinery with a random seed, starting with the drafted team's perks and stat cards
        public static void StartDraft()
        {
            var pick = Drafting.Pending;
            int seed = Drafting.PendingSeed;
            Drafting.Pending = null;
            Core.Daily.Pending = null; Core.Daily.PendingCup = false;
            Mode = RunMode.Normal;
            Loop = 1;
            Daily = Drafting.SeedKey(seed);
            Draft = pick.Team.Id;
            DailyCounts = false;
            var gm = GameManager.Instance;
            foreach (var stat in gm.playerStats.statUpgrades) stat.currentLevel = 0;
            gm.playerStats.techniques.Clear();
            Engine.StatCards.ResetRun();
            LimitBreakEffects.ResetLimits();
            GameManager.statOverload = false;
            StatTracker.runTimer = 0f;
            TutorialManager.onTutorial = false;
            GameManager.gameNumber = 1;
            foreach (var id in pick.Perks) { var t = PerkRegistry.Get(id); if (t != null) gm.playerStats.techniques.Add(t); }
            foreach (var id in pick.Cards) { var c = Engine.StatCards.Get(id); if (c != null) Engine.StatCards.Take(c); }
            Records.DraftStarted();
            Plugin.Log.LogInfo($"Draft cup started: {pick.Team.Name} with {string.Join(", ", pick.Perks)} + {string.Join(", ", pick.Cards)} (power {pick.Power:F3}, seed {seed})");
        }

        // a daily cup ends: lost a match, or won the final (8)
        public static void EndCup(int won)
        {
            if (Daily == null || Mode != RunMode.Normal) return;
            if (Draft != null) { if (won >= 8) Records.DraftWon(); return; }
            Core.Daily.Finish(Core.Daily.Key(Daily, true), DailyCounts, won);
            DailyCounts = false;
        }

        public static void EndInfinite()
        {
            if (Daily != null) { Core.Daily.Finish(Core.Daily.Key(Daily, false), DailyCounts, InfiniteWins); DailyCounts = false; return; }   // its own records
            if (GameManager.Instance != null && GameManager.Instance.numberOfPlayers == 2) { Records.CoopInfiniteRun(InfiniteWins); return; }
            if (InfiniteWins > Save.bestInfiniteWins) Save.bestInfiniteWins = InfiniteWins;
            WriteSave();
            Plugin.Log.LogInfo($"Infinite run over: {InfiniteWins} wins (best {Save.bestInfiniteWins})");
        }

        public static void EnterLoop(int loop)
        {
            Mode = RunMode.Loop;
            Loop = loop;
            if (loop > Save.bestLoop) Save.bestLoop = loop;
            WriteSave();
            Plugin.Log.LogInfo($"Loop {loop} started");
        }
    }
}

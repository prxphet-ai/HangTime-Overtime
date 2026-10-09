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
            Save.infiniteRuns++;
            WriteSave();
            Plugin.Log.LogInfo("Infinite run started");
        }

        public static void EndInfinite()
        {
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

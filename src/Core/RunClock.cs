using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HangtimeOvertime.Core
{
    // The run's own clock (speedruns, community tournaments) and its match log (the leaderboards' cheat checks).
    // Timed: real time from the run's start (the first match's loading) to its last match's final point, including
    // every match, the upgrade screens, the bracket screen and the loading between them; not the pause menu, not the
    // title screen. Unaffected by slow motion or game-speed perks (real time, not game time). Saved with the run.
    internal static class RunClock
    {
        public static float Seconds;
        public static bool Running;
        public static bool Tainted;                 // a modified run (dev keys, debug options, difficulty): it doesn't submit
        public static string TaintReason = "";
        public static readonly List<string> Log = new List<string>();   // "slot,playerPoints,opponentPoints,seconds,opponent"
        private static int matchSlot = -1;
        private static float matchStart;

        public static void Start()
        {
            Seconds = 0f; Running = true; Log.Clear(); matchSlot = -1;
            Tainted = false; TaintReason = "";
            CheckTaint();
        }

        public static void Stop() => Running = false;

        public static void Restore(float seconds, string log, bool tainted, string why)
        {
            Seconds = seconds; Running = true; Log.Clear(); matchSlot = -1;
            Log.AddRange((log ?? "").Split(';').Where(e => e.Length > 0));
            Tainted = tainted; TaintReason = why ?? "";
            CheckTaint();
        }

        public static string LogText => string.Join(";", Log);

        // NetRunner, every frame
        public static void Tick()
        {
            if (!Running || TitleController.onTitle || PauseController.paused) return;
            Seconds += Mathf.Min(Time.unscaledDeltaTime, 1f);           // a hitch (alt-tab, a loading freeze) counts at most a second
            if (Time.frameCount % 60 == 0) CheckTaint();
        }

        public static void MatchStarted(int slot) { matchSlot = slot; matchStart = Seconds; }

        public static void MatchEnded(int playerPoints, int opponentPoints, string opponent)
        {
            if (!Running || matchSlot < 0) return;
            Log.Add($"{matchSlot},{playerPoints},{opponentPoints},{(Seconds - matchStart).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)},{(opponent ?? "").Replace(",", " ").Replace(";", " ")}");
            matchSlot = -1;
        }

        private static void CheckTaint()
        {
            string why = null;
            // development: leaderboard tests against a local server (DevKeys + -ovt-boardtest) submit anyway
            if (Plugin.DevKeys.Value && Array.IndexOf(Environment.GetCommandLineArgs(), "-ovt-boardtest") >= 0) return;
            if (Plugin.DevKeys.Value) why = "DEV KEYS ON";
            else if (Plugin.DumpScenes.Value) why = "DEBUG LOGGING ON";
            else if (Plugin.InfiniteRoundOffset.Value != 0) why = "INFINITE DIFFICULTY CHANGED";
            else if (Environment.GetCommandLineArgs().Any(a => a.StartsWith("-ovt-"))) why = "DEVELOPMENT OPTIONS";
            if (why != null && !Tainted) { Tainted = true; TaintReason = why; Plugin.Log.LogInfo("Run clock: this run won't submit (" + why + ")"); }
        }

        public static string Format(float s)
        {
            int m = (int)(s / 60f);
            float r = s - m * 60;
            return m >= 60 ? $"{m / 60}:{m % 60:00}:{r:00.0}" : $"{m}:{r:00.0}";
        }
    }
}

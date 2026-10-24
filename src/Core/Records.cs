using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace HangtimeOvertime.Core
{
    // The mod's lifetime stats (HangtimeOvertime_stats.json next to the game's save; never the game's save itself):
    // win streaks per mode (current and best), wins and losses per mode, runs completed, the highest Infinite round.
    // Arrays instead of dictionaries: JsonUtility (the game's JSON) only writes plain arrays from a mod's classes.
    [Serializable]
    public class RecordsData
    {
        public int version = 1;
        public string[] modes = new string[0];
        public int[] streak = new int[0], best = new int[0], wins = new int[0], losses = new int[0];
        public int classicRunsWon, infiniteBestRound, versusMatches;
        public int draftsPlayed, draftCupsWon;
        public int coopInfiniteBest;                 // most wins in one co-op Infinite run (local or online)
    }

    internal static class Records
    {
        // mode keys (also the labels on the stats page)
        public const string Classic = "Classic", Coop = "Co-op", Infinite = "Infinite", VersusLocal = "Versus (local)", VersusOnline = "Versus (online)", Draft = "Draft", CoopInfinite = "Co-op Infinite", Community = "Community";

        private static RecordsData data;
        private static string PathOf => Path.Combine(Application.persistentDataPath, "HangtimeOvertime_stats.json");
        public static RecordsData Data { get { Load(); return data; } }

        public static void Load()
        {
            if (data != null) return;
            try { data = File.Exists(PathOf) ? JsonUtility.FromJson<RecordsData>(File.ReadAllText(PathOf)) : null; }
            catch (Exception e) { Plugin.Log.LogWarning("Stats file unreadable, starting fresh: " + e.Message); }
            if (data == null) data = new RecordsData();
            int n = data.modes.Length;
            // older files: every array as long as the mode list
            Array.Resize(ref data.streak, n); Array.Resize(ref data.best, n); Array.Resize(ref data.wins, n); Array.Resize(ref data.losses, n);
        }

        public static void Save()
        {
            try
            {
                var tmp = PathOf + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(Data, true));
                if (File.Exists(PathOf)) File.Delete(PathOf);
                File.Move(tmp, PathOf);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't save stats: " + e.Message); }
        }

        private static int Index(string mode)
        {
            var d = Data;
            int i = Array.IndexOf(d.modes, mode);
            if (i >= 0) return i;
            int n = d.modes.Length + 1;
            Array.Resize(ref d.modes, n); Array.Resize(ref d.streak, n); Array.Resize(ref d.best, n); Array.Resize(ref d.wins, n); Array.Resize(ref d.losses, n);
            d.modes[n - 1] = mode;
            return n - 1;
        }

        public static int Current(string mode) { int i = Array.IndexOf(Data.modes, mode); return i >= 0 ? data.streak[i] : 0; }
        public static int Best(string mode) { int i = Array.IndexOf(Data.modes, mode); return i >= 0 ? data.best[i] : 0; }

        // a match in that mode ended; returns the streak it leaves (0 after a loss) and the streak before
        public static (int now, int before) Record(string mode, bool won)
        {
            int i = Index(mode);
            int before = data.streak[i];
            if (won) { data.wins[i]++; data.streak[i]++; data.best[i] = Math.Max(data.best[i], data.streak[i]); }
            else { data.losses[i]++; data.streak[i] = 0; }
            Save();
            Plugin.Log.LogInfo($"Stats: {mode} {(won ? "win" : "loss")}, streak {data.streak[i]} (best {data.best[i]})");
            return (data.streak[i], before);
        }

        public static void InfiniteRound(int round) { if (round > Data.infiniteBestRound) { data.infiniteBestRound = round; Save(); } }
        public static void ClassicRunWon() { Data.classicRunsWon++; Save(); }
        public static void CoopInfiniteRun(int wins)
        {
            if (wins > Data.coopInfiniteBest) { data.coopInfiniteBest = wins; Save(); }
            Plugin.Log.LogInfo($"Co-op Infinite run over: {wins} wins (best {data.coopInfiniteBest})");
        }

        public static void DraftStarted() { Data.draftsPlayed++; Save(); }
        public static void DraftWon() { Data.draftCupsWon++; Save(); }

        // "WIN STREAK 3 · BEST 7" / after a loss: "STREAK ENDED AT 3 · BEST 7"
        public static string Line(string mode, bool won, int before)
        {
            int best = Best(mode);
            if (won) return $"WIN STREAK {Current(mode)}  ·  BEST {best}";
            return before > 0 ? $"STREAK ENDED AT {before}  ·  BEST {best}" : $"BEST STREAK {best}";
        }
    }
}

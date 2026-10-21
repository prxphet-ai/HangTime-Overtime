using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace HangtimeOvertime.Core
{
    // The daily run: an Infinite run seeded by the UTC date, so everyone gets the same opponents (and their builds) and
    // the same card offers that day (offers skip cards you already own, so different picks change later offers). The
    // first run of a day counts; more runs that day are practice. Results: HangtimeOvertime_daily.json.
    [Serializable]
    public class DailyData
    {
        public int version = 1;
        public string[] dates = new string[0];            // yyyy-MM-dd (UTC)
        public int[] first = new int[0];                  // wins of the run that counts (-1 while it's still going)
        public int[] best = new int[0];                   // best of all that day's runs
        public int[] runs = new int[0];                   // runs started that day
    }

    internal static class Daily
    {
        public static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public static TimeSpan UntilNext => DateTime.UtcNow.Date.AddDays(1) - DateTime.UtcNow;
        public static string Pending;                     // the title's DAILY: the next Infinite start is this day's run

        // FNV-1a over the date: the same number on every PC
        public static uint Seed(string date)
        {
            uint h = 2166136261;
            foreach (char c in "hangtime-overtime-daily-" + date) { h ^= c; h *= 16777619; }
            return h;
        }

        public static int Mix(string date, int purpose, int n)
        {
            uint h = Seed(date);
            h ^= (uint)purpose * 0x9E3779B1u; h *= 16777619;
            h ^= (uint)n * 0x85EBCA77u; h *= 16777619;
            return (int)(h & 0x7FFFFFFF);
        }

        // ------------------------------------------------------------ the game's random numbers, seeded for a moment

        private static readonly Stack<UnityEngine.Random.State> saved = new Stack<UnityEngine.Random.State>();

        public static void Push(int purpose, int n)
        {
            saved.Push(UnityEngine.Random.state);
            UnityEngine.Random.InitState(Mix(RunState.Daily, purpose, n));
        }

        public static void Pop() { if (saved.Count > 0) UnityEngine.Random.state = saved.Pop(); }

        // ------------------------------------------------------------ results

        private static DailyData data;
        private static string PathOf => Path.Combine(Application.persistentDataPath, "HangtimeOvertime_daily.json");
        public static DailyData Data { get { Load(); return data; } }

        private static void Load()
        {
            if (data != null) return;
            try { data = File.Exists(PathOf) ? JsonUtility.FromJson<DailyData>(File.ReadAllText(PathOf)) : null; }
            catch (Exception e) { Plugin.Log.LogWarning("Daily file unreadable, starting fresh: " + e.Message); }
            if (data == null) data = new DailyData();
            int n = data.dates.Length;
            Array.Resize(ref data.first, n); Array.Resize(ref data.best, n); Array.Resize(ref data.runs, n);
        }

        private static void Save()
        {
            try
            {
                var tmp = PathOf + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(Data, true));
                if (File.Exists(PathOf)) File.Delete(PathOf);
                File.Move(tmp, PathOf);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't save daily results: " + e.Message); }
        }

        private static int Index(string date, bool create)
        {
            var d = Data;
            int i = Array.IndexOf(d.dates, date);
            if (i >= 0 || !create) return i;
            int n = d.dates.Length + 1;
            Array.Resize(ref d.dates, n); Array.Resize(ref d.first, n); Array.Resize(ref d.best, n); Array.Resize(ref d.runs, n);
            d.dates[n - 1] = date; d.first[n - 1] = -1; d.best[n - 1] = 0;
            return n - 1;
        }

        public static int Runs(string date) { int i = Index(date, false); return i >= 0 ? data.runs[i] : 0; }
        public static int First(string date) { int i = Index(date, false); return i >= 0 ? data.first[i] : -1; }
        public static int Best(string date) { int i = Index(date, false); return i >= 0 ? data.best[i] : 0; }

        // a daily run starts: true if it's the one that counts (the day's first)
        public static bool Begin(string date)
        {
            int i = Index(date, true);
            data.runs[i]++;
            Save();
            bool counts = data.runs[i] == 1;
            Plugin.Log.LogInfo($"Daily run {date} started (run {data.runs[i]}, {(counts ? "counts" : "practice")}, seed {Seed(date)})");
            return counts;
        }

        // a daily run is over (lost a match)
        public static void Finish(string date, bool counts, int wins)
        {
            int i = Index(date, true);
            if (counts) data.first[i] = wins;
            data.best[i] = Math.Max(data.best[i], wins);
            Save();
            Plugin.Log.LogInfo($"Daily run {date} over: {wins} wins ({(counts ? "counts" : "practice")}; best that day {data.best[i]})");
        }

        // newest first
        public static IEnumerable<(string date, int first, int best, int runs)> History() =>
            Data.dates.Select((d, i) => (d, data.first[i], data.best[i], data.runs[i])).OrderByDescending(x => x.d, StringComparer.Ordinal);

        // ------------------------------------------------------------ the day's card offers

        public static readonly Queue<Logo_technique> PerkOffer = new Queue<Logo_technique>();
        public static readonly Queue<UpgradePair> CardOffer = new Queue<UpgradePair>();

        // the first `count` distinct cards (by key) that pass `ok`, in the day's order for this match; a card listed k
        // times (the game's rarity weighting) comes early k times as often (exponential sort keys)
        public static List<T> Pick<T>(IEnumerable<T> cards, Func<T, string> key, Func<T, bool> ok, int count, int purpose)
        {
            int n = RunState.InfiniteMatch;
            return cards.Where(c => c != null).GroupBy(key).Select(g => (c: g.First(), w: g.Count()))
                .OrderBy(x => -Math.Log((Mix(RunState.Daily, purpose * 1000 + n, (int)(StableHash(key(x.c)) & 0x7FFFFFFF)) + 1.0) / 2147483649.0) / x.w)
                .Select(x => x.c).Where(ok).Take(count).ToList();
        }

        public static uint StableHash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s ?? "") { h ^= c; h *= 16777619; }
            return h;
        }
    }
}

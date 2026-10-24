using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HangtimeOvertime.Core;
using UnityEngine;
using Rules = HangtimeOvertime.Generated.Community;

namespace HangtimeOvertime.Online
{
    // The global boards: CLASSIC SPEEDRUN (fastest Classic clear, RunClock) and INFINITE (most rounds won in one solo
    // Infinite run). A result is checked here against the same rules the server uses, queued in
    // HangtimeOvertime_pending.json and sent when the server answers (now, or the next time the game is online).
    // Boards are per major.minor mod version (0.4.x players share one): balance changes between versions would make
    // old results unfair. Modified runs (dev keys, debug options, Infinite difficulty) never submit.
    [Serializable]
    public class PendingData { public string[] items = new string[0]; }

    [Serializable]
    public class BoardReply
    {
        public bool ok; public string error;
        public int[] ranks; public string[] ids; public string[] names; public float[] values; public double[] dates; public string[] mods;
        public int meRank; public float meValue; public double meDate; public int total;
    }

    [Serializable] public class ScoreReply { public bool ok; public string error; public bool best; public int rank; public float value; }

    internal static class Leaderboards
    {
        public const string Classic = "classic", Infinite = "infinite";
        public static string LastStatus = "";               // what happened to the last result (shown on the boards screen)
        private static string PathOf => Path.Combine(Application.persistentDataPath, "HangtimeOvertime_pending.json");
        private static bool sending;
        private static float nextTry;

        public static string Format(string board, float value) => board == Classic ? RunClock.Format(value) : $"{(int)value} {((int)value == 1 ? "ROUND" : "ROUNDS")}";

        private static List<string> Pending()
        {
            try { return File.Exists(PathOf) ? (JsonUtility.FromJson<PendingData>(File.ReadAllText(PathOf))?.items ?? new string[0]).ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }

        private static void SavePending(List<string> items)
        {
            try { File.WriteAllText(PathOf, JsonUtility.ToJson(new PendingData { items = items.ToArray() }, true)); }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't save pending results: " + e.Message); }
        }

        public static int PendingCount => Pending().Count;

        // a qualifying run ended
        public static void Finish(string board, float value, string log, string seed)
        {
            string shown = Format(board, value);
            if (RunClock.Tainted) { Report($"{shown}: NOT SUBMITTED ({RunClock.TaintReason})"); return; }
            if (!Plugin.CommunitySubmit.Value) { Report($"{shown}: NOT SUBMITTED (SUBMITTING IS OFF IN SETTINGS)"); return; }
            string why = Check(board, value, log);
            if (why != null) { Report($"{shown}: NOT SUBMITTED ({why})"); return; }
            var items = Pending();
            while (items.Count >= 20) items.RemoveAt(0);                  // a long time offline: keep the newest 20
            items.Add(Community.Json("board", board, "value", value, "log", log, "mod", Plugin.Version, "seed", seed ?? "", "at", DateTime.UtcNow.ToString("o")));
            SavePending(items);
            Report($"{shown}: " + (Community.Configured ? "SUBMITTING..." : "SAVED: IT SUBMITS WHEN THE COMMUNITY SERVER IS SET UP"));
            nextTry = 0f;
            Flush();
        }

        private static void Report(string text)
        {
            LastStatus = text;
            Plugin.Log.LogInfo("Leaderboard: " + text);
            Patches.Toast.Show("LEADERBOARD: " + text);
        }

        // the same checks the server makes (a result that would be refused isn't kept)
        public static string Check(string board, float value, string log)
        {
            var m = (log ?? "").Split(';').Where(e => e.Length > 0).Select(e => e.Split(',')).Where(p => p.Length >= 4).Select(p =>
                (slot: int.TryParse(p[0], out var s) ? s : -1, pp: int.TryParse(p[1], out var a) ? a : 0, o: int.TryParse(p[2], out var b) ? b : 0,
                 sec: float.TryParse(p[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var c) ? c : 0f)).ToList();
            float total = m.Sum(x => x.sec);
            if (board == Classic)
            {
                if (value < Rules.ClassicMinSeconds || value > Rules.ClassicMaxSeconds) return "IMPOSSIBLE TIME";
                for (int g = 1; g <= 8; g++)
                {
                    var x = m.FirstOrDefault(e => e.slot == g);
                    if (x.slot != g || x.pp <= x.o) return $"MATCH {g} MISSING FROM THE RUN LOG";
                    if (x.sec < Rules.MatchMinSeconds) return $"MATCH {g} IMPOSSIBLY SHORT";
                }
                return total > value + 5 ? "MATCH TIMES DON'T ADD UP" : null;
            }
            int wins = m.Count(x => x.pp > x.o);
            if (value < 1) return "NO ROUNDS WON";
            if (value > Rules.InfiniteMaxRounds || wins != (int)value) return "THE RUN LOG DOESN'T MATCH";
            if (total < value * Rules.InfiniteSecondsPerRound) return "TOO FAST";
            return null;
        }

        // sends the queue, oldest first; each answered item (accepted or refused) leaves it, unreachable ones stay
        public static void Flush()
        {
            if (sending || !Community.Configured || Time.realtimeSinceStartup < nextTry) return;
            var items = Pending();
            if (items.Count == 0) return;
            if (!Identity.HasName) { LastStatus = "SET A NAME ON THE LEADERBOARDS SCREEN TO SUBMIT"; return; }
            sending = true;
            var item = items[0];
            var body = item.TrimEnd().TrimEnd('}') + "," + Community.Json("name", Identity.Name).TrimStart('{');
            Community.Post("/v1/scores", body, (text, err) =>
            {
                sending = false;
                if (err != null && !Community.Reachable) { nextTry = Time.realtimeSinceStartup + 60f; LastStatus = "SAVED: SUBMITS WHEN THE GAME IS ONLINE AGAIN"; return; }
                var list = Pending(); list.Remove(item); SavePending(list);
                var board = item.Contains("\"board\":\"classic\"") ? Classic : Infinite;
                if (err != null) { Report("REFUSED: " + err); return; }
                var r = JsonUtility.FromJson<ScoreReply>(text);
                Report(r.best ? $"{Format(board, r.value)} SUBMITTED · RANK #{r.rank}" : $"SUBMITTED · YOUR BEST IS STILL {Format(board, r.value)} (#{r.rank})");
                if (list.Count > 0) Flush();
            });
        }

        // NetRunner: try the queue now and then (title screen)
        public static void Tick() { if (TitleController.onTitle && Time.frameCount % 600 == 0) Flush(); }

        public static void Board(string board, Action<BoardReply, string> done)
        {
            Community.Get($"/v1/scores?board={board}&version={Community.BoardVersion(Plugin.Version)}&player={Identity.Id}", (text, err) =>
                done(err == null ? JsonUtility.FromJson<BoardReply>(text) : null, err));
        }
    }
}

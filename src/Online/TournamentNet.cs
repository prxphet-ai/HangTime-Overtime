using System;
using HangtimeOvertime.Core;
using UnityEngine;

namespace HangtimeOvertime.Online
{
    [Serializable]
    public class BrowseReply
    {
        public bool ok; public string error;
        public string[] ids, names, creators, owners, modes, mods; public int[] matches, plays, clears, ratings; public float[] difficulty, rating; public double[] created;
    }

    [Serializable]
    public class TourReply
    {
        public bool ok; public string error;
        public string id, name, description, creator, owner, mode, code, hash, mod; public int matches, plays, clears, ratings, hidden; public float difficulty, rating;
    }

    [Serializable] public class PublishReply { public bool ok; public string error; public string id; public bool existing, updated; public int difficulty; }

    [Serializable]
    public class TourScores
    {
        public bool ok; public string error;
        public int[] ranks; public string[] ids, names; public float[] values; public double[] dates; public int meRank; public float meValue;
    }

    // The community tournament browser's server calls (server/worker.js /v1/tournaments). Results of a downloaded
    // tournament's runs go through the leaderboards' offline queue, so they're sent later when the game is offline.
    internal static class TournamentNet
    {
        public static readonly string[] Sorts = { "new", "played", "liked", "hard", "easy" };
        public static readonly string[] SortNames = { "NEWEST", "MOST PLAYED", "TOP RATED", "HARDEST", "EASIEST" };

        public static void List(string sort, string mode, bool mine, int offset, Action<BrowseReply, string> done) =>
            Community.Get($"/v1/tournaments?sort={sort}&offset={offset}" + (mode != null ? "&mode=" + mode : "") + (mine ? "&owner=" + Identity.Id : ""),
                (text, err) => done(err == null ? JsonUtility.FromJson<BrowseReply>(text) : null, err));

        public static void Fetch(string id, Action<TourReply, string> done) =>
            Community.Get("/v1/tournaments/" + id, (text, err) => done(err == null ? JsonUtility.FromJson<TourReply>(text) : null, err));

        public static void Scores(string id, Action<TourScores, string> done) =>
            Community.Get($"/v1/tournaments/{id}/scores?player={Identity.Id}", (text, err) => done(err == null ? JsonUtility.FromJson<TourScores>(text) : null, err));

        // publish (or update the published version of) one of this player's own cleared tournaments
        public static void Publish(TourEntry e, Action<string> done)
        {
            if (!Identity.HasName) { done("SET YOUR NAME FIRST (LEADERBOARDS SCREEN)"); return; }
            string path = "/v1/tournaments" + (string.IsNullOrEmpty(e.remote) ? "" : "/" + e.remote);
            Community.Post(path, Community.Json("name", Identity.Name, "code", Tournaments.Code(e), "mod", Plugin.Version), (text, err) =>
            {
                if (err != null) { done(err); return; }
                var r = JsonUtility.FromJson<PublishReply>(text);
                e.remote = r.id; e.published = e.Hash; e.creator = Identity.Name;
                Tournaments.Save();
                Plugin.Log.LogInfo($"Tournament {(r.updated ? "updated" : r.existing ? "already published" : "published")}: {r.id} ({e.Hash}, difficulty {r.difficulty})");
                done(null);
            });
        }

        public static void Unpublish(TourEntry e, Action<string> done)
        {
            Community.Post($"/v1/tournaments/{e.remote}/delete", "{}", (text, err) =>
            {
                if (err == null || err.Contains("NO SUCH")) { Plugin.Log.LogInfo("Tournament removed online: " + e.remote); e.remote = ""; e.published = ""; Tournaments.Save(); done(null); }
                else done(err);
            });
        }

        public static void Rate(string id, int stars, Action<string> done) => Community.Post($"/v1/tournaments/{id}/rate", Community.Json("stars", stars), (t, err) => done(err));

        public static void Report(string id, Action<string> done) => Community.Post($"/v1/tournaments/{id}/report", Community.Json("reason", "player report from the browser"), (t, err) => done(err));

        // a finished attempt at a downloaded / published tournament: counts its plays and clears and its fastest-clear board
        public static void Result(Tournaments.Play p, bool cleared, float seconds, string log)
        {
            var e = p.Entry;
            if (string.IsNullOrEmpty(e.remote) || e.published.Length > 0 && e.published != e.Hash) return;   // not online, or edited since
            if (RunClock.Tainted || !Plugin.CommunitySubmit.Value) return;
            Leaderboards.Queue(Community.Json("_path", $"/v1/tournaments/{e.remote}/result", "cleared", cleared, "seconds", seconds, "log", log, "hash", e.Hash, "mod", Plugin.Version));
        }
    }
}

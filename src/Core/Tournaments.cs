using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using HangtimeOvertime.Engine;
using HangtimeOvertime.Generated;
using UnityEngine;
using Rules = HangtimeOvertime.Generated.Community;
using StatCards = HangtimeOvertime.Engine.StatCards;

namespace HangtimeOvertime.Core
{
    // A community tournament: 1-8 matches against custom teams (a vanilla squad's body and AI, chosen colours, an emblem,
    // stat levels 0-3, Overtime perks and stat cards), FIXED (the creator also sets the player's levels, perks and cards;
    // no picks during the run) or DRAFT (the normal upgrade screens). Field names are the share code's JSON keys, which
    // the server checks too (server/worker.js checkTournament): change them only with a new format number.
    [Serializable]
    public class TourEmblem
    {
        public string mascot = "star", layout = "top", font = "bangers", accent = "FFD95A";
    }

    [Serializable]
    public class TourTeam
    {
        public string name = "", intro = "", @base = "Rensho";
        public string jersey = "E8452A", shorts = "7A1E10", banner = "C8321E";
        public string[] hair = { "FFB000", "FF5A1A" };
        public TourEmblem emblem = new TourEmblem();
        public int[] levels = new int[7];                 // Tournaments.Stats order
        public string[] perks = new string[0], cards = new string[0];
    }

    [Serializable]
    public class TourLoadout
    {
        public int[] levels = new int[7];
        public string[] perks = new string[0], cards = new string[0];
    }

    [Serializable]
    public class Tournament
    {
        public int v = 1;
        public string name = "", desc = "", mode = "fixed";
        public TourTeam[] teams = new TourTeam[0];
        public TourLoadout player = new TourLoadout();
        public bool Fixed => mode == "fixed";
    }

    // the creator's clear of exactly this content (its hash): the match log the server checks before publishing
    [Serializable]
    public class TourVerify
    {
        public string hash = "", log = "", mod = "";
        public float seconds;
    }

    // one tournament on this PC: made here or added from a code / the browser, with this player's record on it
    [Serializable]
    public class TourEntry
    {
        public string content = "", verify = "";          // the share code's parts (base64url deflate), kept verbatim
        public bool mine;
        public string remote = "", creator = "";         // the server's id once published / where it came from
        public int attempts, clears;
        public float best;                                // fastest clear (s), 0 = none
        public long added;
        public string Hash => Tournaments.HashOf(content);
    }

    // on disk each entry is its own JSON string (JsonUtility won't nest a mod's classes)
    [Serializable]
    internal class TourFile { public string[] items = new string[0]; }

    internal static class Tournaments
    {
        public const int Format = 1;
        public static readonly string[] Stats = { "Spike", "Jump", "Block", "Bump", "SpinServe", "ServeJump", "FloatServe" };
        public static readonly string[] Layouts = { "top", "stacked", "split" };
        public static readonly string[] Fonts = { "bangers", "marker" };
        public const int IntroMax = 60;
        public static string[] Bases => Rules.RatingBases;            // the ten vanilla squads, regular -> combo -> boss

        // ------------------------------------------------------------ share codes: OVT1.<content>.<verify>.<check>

        public static string Encode(Tournament t) => B64(Deflate(Encoding.UTF8.GetBytes(ToJson(t))));
        public static string EncodeVerify(TourVerify v) => v == null ? "" : B64(Deflate(Encoding.UTF8.GetBytes(JsonUtility.ToJson(v))));
        public static string HashOf(string content) => Online.Identity.Sha256(content ?? "").Substring(0, 16);

        public static string Code(TourEntry e) => Code(e.content, e.verify);
        public static string Code(string content, string verify)
        {
            string head = $"OVT{Format}.{content}.{verify ?? ""}";
            return head + "." + Online.Identity.Sha256(head).Substring(0, 8);
        }

        // the code's tournament (and the creator's clear, if it carries one), or why not
        public static string Decode(string code, out Tournament t, out TourEntry entry)
        {
            t = null; entry = null;
            code = new string((code ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
            var parts = code.Split('.');
            if (parts.Length != 4 || !parts[0].StartsWith("OVT")) return "THAT ISN'T A TOURNAMENT CODE";
            if (!int.TryParse(parts[0].Substring(3), out int fmt)) return "THAT ISN'T A TOURNAMENT CODE";
            if (fmt > Format) return "MADE WITH A NEWER OVERTIME: UPDATE THE MOD TO PLAY IT";
            if (code.Length > Rules.CodeMaxChars) return "THE CODE IS TOO LONG";
            if (Online.Identity.Sha256($"{parts[0]}.{parts[1]}.{parts[2]}").Substring(0, 8) != parts[3]) return "THE CODE IS DAMAGED OR INCOMPLETE (CHECK THE COPY)";
            try { t = FromJson(Encoding.UTF8.GetString(Inflate(UnB64(parts[1])))); }
            catch { return "THE CODE IS DAMAGED"; }
            if (t == null) return "THE CODE IS DAMAGED";
            if (t.v > Format) return "MADE WITH A NEWER OVERTIME: UPDATE THE MOD TO PLAY IT";
            Normalize(t);
            var why = Problem(t);
            if (why != null) return why;
            entry = new TourEntry { content = parts[1], verify = VerifyOf(parts[2], HashOf(parts[1])) != null ? parts[2] : "", added = Now };
            return null;
        }

        public static Tournament Content(TourEntry e)
        {
            try { var t = FromJson(Encoding.UTF8.GetString(Inflate(UnB64(e.content)))); Normalize(t); return t; }
            catch { return null; }
        }

        // the clear record, if it belongs to this content
        public static TourVerify VerifyOf(string verify, string hash)
        {
            if (string.IsNullOrEmpty(verify)) return null;
            try { var v = JsonUtility.FromJson<TourVerify>(Encoding.UTF8.GetString(Inflate(UnB64(verify)))); return v != null && v.hash == hash ? v : null; }
            catch { return null; }
        }

        public static bool Verified(TourEntry e) => VerifyOf(e.verify, e.Hash) != null;

        // ------------------------------------------------------------ JSON (the share code's content; the server reads the same keys)

        public static string ToJson(Tournament t)
        {
            object Emb(TourEmblem e) => new Dictionary<string, object> { { "mascot", e.mascot }, { "layout", e.layout }, { "font", e.font }, { "accent", e.accent } };
            object Team(TourTeam m) => new Dictionary<string, object>
            {
                { "name", m.name }, { "intro", m.intro }, { "base", m.@base }, { "jersey", m.jersey }, { "shorts", m.shorts }, { "banner", m.banner },
                { "hair", m.hair }, { "emblem", Emb(m.emblem) }, { "levels", m.levels }, { "perks", m.perks }, { "cards", m.cards },
            };
            var d = new Dictionary<string, object>
            {
                { "v", t.v }, { "name", t.name }, { "desc", t.desc }, { "mode", t.mode }, { "teams", t.teams.Select(Team).ToList() },
                { "player", new Dictionary<string, object> { { "levels", t.player.levels }, { "perks", t.player.perks }, { "cards", t.player.cards } } },
            };
            return MiniJson.Write(d);
        }

        public static Tournament FromJson(string json)
        {
            var d = MiniJson.Obj(MiniJson.Parse(json));
            var t = new Tournament { v = MiniJson.I(d, "v"), name = MiniJson.S(d, "name"), desc = MiniJson.S(d, "desc"), mode = MiniJson.S(d, "mode") };
            t.teams = MiniJson.Arr(MiniJson.Get(d, "teams")).Select(o =>
            {
                var m = MiniJson.Obj(o);
                var e = MiniJson.Obj(MiniJson.Get(m, "emblem"));
                return new TourTeam
                {
                    name = MiniJson.S(m, "name"), intro = MiniJson.S(m, "intro"), @base = MiniJson.S(m, "base"),
                    jersey = MiniJson.S(m, "jersey"), shorts = MiniJson.S(m, "shorts"), banner = MiniJson.S(m, "banner"), hair = MiniJson.Strings(MiniJson.Get(m, "hair")),
                    emblem = new TourEmblem { mascot = MiniJson.S(e, "mascot"), layout = MiniJson.S(e, "layout"), font = MiniJson.S(e, "font"), accent = MiniJson.S(e, "accent") },
                    levels = MiniJson.Ints(MiniJson.Get(m, "levels")), perks = MiniJson.Strings(MiniJson.Get(m, "perks")), cards = MiniJson.Strings(MiniJson.Get(m, "cards")),
                };
            }).ToArray();
            var p = MiniJson.Obj(MiniJson.Get(d, "player"));
            t.player = new TourLoadout { levels = MiniJson.Ints(MiniJson.Get(p, "levels")), perks = MiniJson.Strings(MiniJson.Get(p, "perks")), cards = MiniJson.Strings(MiniJson.Get(p, "cards")) };
            return t;
        }

        // missing arrays and lengths and lengths, so the rest of the code can count on them
        public static void Normalize(Tournament t)
        {
            t.name = t.name ?? ""; t.desc = t.desc ?? ""; t.mode = t.mode ?? "fixed";
            t.teams = t.teams ?? new TourTeam[0];
            t.player = t.player ?? new TourLoadout();
            t.player.levels = Fit(t.player.levels); t.player.perks = t.player.perks ?? new string[0]; t.player.cards = t.player.cards ?? new string[0];
            foreach (var tm in t.teams.Where(x => x != null))
            {
                tm.name = tm.name ?? ""; tm.intro = tm.intro ?? "";
                tm.levels = Fit(tm.levels); tm.perks = tm.perks ?? new string[0]; tm.cards = tm.cards ?? new string[0];
                tm.hair = tm.hair ?? new string[0]; tm.emblem = tm.emblem ?? new TourEmblem();
            }
        }

        private static int[] Fit(int[] a) { var r = new int[7]; if (a != null) Array.Copy(a, r, Math.Min(7, a.Length)); return r; }

        private static byte[] Deflate(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                using (var z = new DeflateStream(ms, System.IO.Compression.CompressionMode.Compress, true)) z.Write(data, 0, data.Length);
                return ms.ToArray();
            }
        }

        private static byte[] Inflate(byte[] data)
        {
            using (var z = new DeflateStream(new MemoryStream(data), System.IO.Compression.CompressionMode.Decompress))
            using (var ms = new MemoryStream())
            {
                var buf = new byte[4096];
                int n, total = 0;
                while ((n = z.Read(buf, 0, buf.Length)) > 0) { ms.Write(buf, 0, n); if ((total += n) > 256 * 1024) throw new InvalidDataException("too big"); }
                return ms.ToArray();
            }
        }

        private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static byte[] UnB64(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(s + new string('=', (4 - s.Length % 4) % 4));
        }

        // ------------------------------------------------------------ checks (the server makes the same ones)

        private static bool Hex6(string s) => s != null && s.Length == 6 && s.All(Uri.IsHexDigit);
        private static bool PerkOk(string id) => PerkRegistry.Get(id) != null;
        private static bool CardOk(string id) => StatCards.Get(id) != null;

        public static string Problem(Tournament t)
        {
            if (t.v != Format) return "UNKNOWN TOURNAMENT FORMAT";
            if (t.name.Trim().Length == 0) return "GIVE THE TOURNAMENT A NAME";
            if (t.name.Length > Rules.TournamentNameMax || Online.Community.Blocked(t.name)) return "THE TOURNAMENT NAME ISN'T ALLOWED";
            if (t.desc.Length > Rules.DescriptionMax || Online.Community.Blocked(t.desc)) return "THE DESCRIPTION ISN'T ALLOWED";
            if (t.mode != "fixed" && t.mode != "draft") return "UNKNOWN TOURNAMENT TYPE";
            if (t.teams.Length < Rules.TournamentMinMatches || t.teams.Length > Rules.TournamentMaxMatches) return $"A TOURNAMENT HAS {Rules.TournamentMinMatches}-{Rules.TournamentMaxMatches} MATCHES";
            for (int i = 0; i < t.teams.Length; i++)
            {
                var tm = t.teams[i];
                string n = $"MATCH {i + 1}";
                if (tm == null) return n + ": NO TEAM";
                if (tm.name.Trim().Length == 0) return n + ": GIVE THE TEAM A NAME";
                if (tm.name.Length > Rules.TeamNameMax || Online.Community.Blocked(tm.name)) return n + ": THE TEAM NAME ISN'T ALLOWED";
                if (tm.intro.Length > IntroMax || Online.Community.Blocked(tm.intro)) return n + ": THE TEAM'S LINE ISN'T ALLOWED";
                if (!Bases.Contains(tm.@base)) return n + ": UNKNOWN SQUAD";
                if (!Hex6(tm.jersey) || !Hex6(tm.shorts) || !Hex6(tm.banner) || tm.hair.Length != 2 || !tm.hair.All(Hex6)) return n + ": BAD COLOURS";
                var e = tm.emblem;
                if (!Rules.Mascots.Contains(e.mascot) || !Layouts.Contains(e.layout) || !Fonts.Contains(e.font) || !Hex6(e.accent)) return n + ": BAD EMBLEM";
                if (tm.levels.Any(l => l < 0 || l > Rules.StatMaxLevel)) return n + ": STAT LEVELS ARE 0-" + Rules.StatMaxLevel;
                if (tm.perks.Length > Rules.TeamMaxPerks || !tm.perks.All(PerkOk) || tm.perks.Distinct().Count() != tm.perks.Length) return n + $": UP TO {Rules.TeamMaxPerks} DIFFERENT KNOWN PERKS";
                if (tm.cards.Length > Rules.TeamMaxCards || !tm.cards.All(CardOk)) return n + $": UP TO {Rules.TeamMaxCards} KNOWN STAT CARDS";
            }
            if (t.Fixed)
            {
                var p = t.player;
                if (p.levels.Any(l => l < 0 || l > Rules.StatMaxLevel)) return "YOUR TEAM: STAT LEVELS ARE 0-" + Rules.StatMaxLevel;
                if (p.perks.Length > Rules.LoadoutMaxPerks || !p.perks.All(PerkOk) || p.perks.Distinct().Count() != p.perks.Length) return $"YOUR TEAM: UP TO {Rules.LoadoutMaxPerks} DIFFERENT KNOWN PERKS";
                if (p.cards.Length > Rules.LoadoutMaxCards || !p.cards.All(CardOk)) return $"YOUR TEAM: UP TO {Rules.LoadoutMaxCards} KNOWN STAT CARDS";
            }
            return null;
        }

        // ------------------------------------------------------------ difficulty (tools/tournament_rating.py)

        private static float PerkPower(string id) { int i = Array.IndexOf(Draft.PerkIds, id); return i >= 0 ? Draft.PerkPower[i] : 0f; }
        private static float CardPower(string id) { int i = Array.IndexOf(Draft.CardIds, id); return i >= 0 ? Draft.CardPower[i] : 0f; }

        // the simulator's estimate of the player's chance to win match `index` (0-based)
        public static float WinChance(Tournament t, int index)
        {
            var tm = t.teams[index];
            int b = Array.IndexOf(Rules.RatingBases, tm.@base);
            float pl, pp, pc;
            if (t.Fixed) { pl = t.player.levels.Sum(); pp = t.player.perks.Sum(PerkPower); pc = t.player.cards.Sum(CardPower); }
            else { pl = 0f; pp = (index + 1) / 2 * Rules.RatingDraftPick; pc = index / 2 * Rules.RatingDraftPick; }   // one pick per match won, perk / card in turn
            float logit = Rules.RatingIntercept + (b >= 0 ? Rules.RatingBaseValues[b] : 0f)
                + Rules.RatingTeamLevels * tm.levels.Sum() + Rules.RatingTeamPerks * tm.perks.Sum(PerkPower) + Rules.RatingTeamCards * tm.cards.Sum(CardPower)
                + Rules.RatingPlayerLevels * pl + Rules.RatingPlayerPerks * pp + Rules.RatingPlayerCards * pc;
            return 1f / (1f + Mathf.Exp(-logit));
        }

        public static string Pct(float p) => Mathf.RoundToInt(p * 100f) + "%";

        public static float ClearChance(Tournament t)
        {
            float c = 1f;
            for (int i = 0; i < t.teams.Length; i++) c *= WinChance(t, i);
            return c;
        }

        // 1 (most players clear it) to 10 (about 1 in 100)
        public static int Difficulty(Tournament t) => Mathf.Clamp(Mathf.RoundToInt(1f + 4.5f * -Mathf.Log10(Mathf.Max(1e-4f, ClearChance(t)))), 1, 10);

        // ------------------------------------------------------------ this PC's list

        private static List<TourEntry> file;
        private static string PathOf => Path.Combine(Application.persistentDataPath, "HangtimeOvertime_tournaments.json");
        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public static List<TourEntry> All { get { Load(); return file; } }

        private static void Load()
        {
            if (file != null) return;
            file = new List<TourEntry>();
            try
            {
                var f = File.Exists(PathOf) ? JsonUtility.FromJson<TourFile>(File.ReadAllText(PathOf)) : null;
                foreach (var item in f?.items ?? new string[0])
                {
                    var e = JsonUtility.FromJson<TourEntry>(item);
                    if (e != null && !string.IsNullOrEmpty(e.content)) file.Add(e);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't read tournaments: " + e.Message); }
        }

        public static void Save()
        {
            Load();
            try { File.WriteAllText(PathOf, JsonUtility.ToJson(new TourFile { items = file.Select(e => JsonUtility.ToJson(e)).ToArray() }, true)); }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't save tournaments: " + e.Message); }
        }

        public static TourEntry Find(string hash) => All.FirstOrDefault(x => x.Hash == hash);

        // an added code: the same content already here keeps its record (and gains the clear record if this one has it)
        public static TourEntry Add(TourEntry e)
        {
            var have = Find(e.Hash);
            if (have != null) { if (!Verified(have) && Verified(e)) have.verify = e.verify; Save(); return have; }
            All.Insert(0, e);
            Save();
            return e;
        }

        public static void Remove(TourEntry e) { All.Remove(e); Save(); }

        // a creator's edit: new content (new hash), so the clear record no longer applies; the record of plays restarts
        public static bool Update(TourEntry e, Tournament t)
        {
            string c = Encode(t);
            if (c == e.content) return false;
            e.content = c; e.verify = ""; e.attempts = 0; e.clears = 0; e.best = 0f;
            Save();
            return true;
        }

        // ------------------------------------------------------------ playing one

        public static TourEntry Pending;                  // the next run started from the title (set by the tournament menu)
        public static bool PendingTest;                   // the creator's own test play (its clear becomes the clear record)

        public class Play
        {
            public TourEntry Entry;
            public Tournament T;
            public bool Test;
            public int Matches => T.teams.Length;
            public int FirstSlot => 9 - Matches;          // the last match is the game's final (slot 8), so its win is the game's own
            public int IndexOf(int slot) => slot - FirstSlot;
            public TourTeam TeamAt(int slot) { int i = IndexOf(slot); return i >= 0 && i < Matches ? T.teams[i] : null; }
        }

        public static Play Current;                       // the tournament being played, or null

        // the run's opponent in this slot: a TeamDef for TeamRoster.Dress (the custom id lets the banner find its emblem)
        public static TeamDef DefOf(Play p, int slot)
        {
            var tm = p.TeamAt(slot);
            if (tm == null) return null;
            int i = p.IndexOf(slot);
            string cls = Teams.VanillaBoss.Contains(tm.@base) ? "boss" : Teams.VanillaCombo.Contains(tm.@base) ? "combo" : "regular";
            var w = Teams.VanillaWeights(tm.@base);
            return new TeamDef
            {
                Id = $"custom:{p.Entry.Hash}:{i}", Name = tm.name, Base = tm.@base, Slot = cls, Element = "none",
                Jersey = tm.jersey, Shorts = tm.shorts, Banner = tm.banner, Hair = tm.hair,
                Intro = tm.intro.Length > 0 ? tm.intro : "Let's see what you've got!", Win = "Better luck next time!", Lose = "You earned that one.",
                Comment = $"{tm.name} from {p.T.name}.", GotPoint = new[] { "Point for us!", "Too easy!" }, LostPoint = new[] { "Lucky...", "Not again!" },
                Pitch = 1f, PointsOffset = 0f, Signature = tm.perks,
                WeightStats = w != null ? w.Select(x => x.statName).ToArray() : Stats, Weights = w != null ? w.Select(x => (float)x.weight).ToArray() : Stats.Select(_ => 1f).ToArray(),
            };
        }

        public static TourEmblem EmblemOf(string defId, out string name)
        {
            name = null;
            var p = Current;
            if (p == null || defId == null || !defId.StartsWith("custom:")) return EditorEmblem(defId, out name);
            var bits = defId.Split(':');
            if (bits.Length != 3 || bits[1] != p.Entry.Hash || !int.TryParse(bits[2], out int i) || i < 0 || i >= p.Matches) return null;
            name = p.T.teams[i].name;
            return p.T.teams[i].emblem;
        }

        // the editor's preview banner (not a run): registered by the editor while it shows one
        public static readonly Dictionary<string, (TourEmblem e, string name)> Previews = new Dictionary<string, (TourEmblem, string)>();
        private static TourEmblem EditorEmblem(string id, out string name)
        {
            name = null;
            if (id == null || !Previews.TryGetValue(id, out var v)) return null;
            name = v.name;
            return v.e;
        }
    }
}

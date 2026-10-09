using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HangtimeOvertime.Engine;
using UnityEngine;

namespace HangtimeOvertime.Core
{
    // One saved run: everything needed to put the player back where they were.
    // stage "match"   = the start of match gameNumber/infiniteMatch (closing mid-match resumes here)
    //       "upgrade" = just won a match, the perk/stat screen was showing
    //       "next"    = a pick was made, the next match hadn't started yet
    [Serializable]
    public class RunSnapshot
    {
        public bool has;
        public string mode;                 // Normal | Loop | Infinite
        public string stage;
        public int gameNumber, infiniteMatch, infiniteWins, loop, lineUp;
        public float runTimer;
        public string[] statNames = new string[0];
        public int[] statLevels = new int[0];
        public string[] techniques = new string[0];       // "perk:<id>" (Overtime) or "game:<Technique class>"
        public string[] cardStats = new string[0];
        public float[] cardPoints = new float[0];
        public bool[] limits = new bool[6];               // spike, bump, jump, spin serve, float serve, block
        public bool statOverload;
        public string opponentKey;                        // new-team id or the game's team prefab name
        public string opponentName;
        public string[] opponentPerks = new string[0];
        public string savedAt;
    }

    // On disk each slot is its own JSON object (Unity's JsonUtility won't nest custom classes from a mod assembly).
    [Serializable]
    public class RunSaveFile
    {
        public int version = RunSaves.Version;
        public string infinite = "";
        public string classic = "";
        public string[] titleClasses = new string[0];      // the game's card titles, see PerkRegistry.KnownTitles
        public string[] titleNames = new string[0];
        public string[] descClasses = new string[0];       // and their card descriptions (Versus offers show them)
        public string[] descTexts = new string[0];
    }

    internal class RunSlots
    {
        public RunSnapshot infinite = new RunSnapshot();
        public RunSnapshot classic = new RunSnapshot();
    }

    internal static class RunSaves
    {
        public const int Version = 1;
        private static RunSlots file = new RunSlots();
        public static string LoadProblem;          // shown on the title screen when a save could not be read
        private static string PathOf => Path.Combine(Application.persistentDataPath, "HangtimeOvertime_runs.json");

        public static RunSnapshot Infinite => file.infinite;
        public static RunSnapshot Classic => file.classic;
        public static bool Has(bool infinite) => (infinite ? file.infinite : file.classic).has;

        // ------------------------------------------------------------ file

        public static void Load()
        {
            LoadProblem = null;
            try
            {
                if (!File.Exists(PathOf)) return;
                var loaded = JsonUtility.FromJson<RunSaveFile>(File.ReadAllText(PathOf));
                if (loaded == null) throw new Exception("empty file");
                if (loaded.version > Version) throw new Exception($"made by a newer version of the mod (save v{loaded.version}, mod v{Version})");
                file = Migrate(loaded);
                Plugin.Log.LogInfo($"Saved runs: Infinite {(file.infinite.has ? RunSaves.Summary(file.infinite) : "none")}, Classic {(file.classic.has ? RunSaves.Summary(file.classic) : "none")}");
            }
            catch (Exception e)
            {
                LoadProblem = "Saved run could not be loaded: " + e.Message;
                Plugin.Log.LogWarning(LoadProblem);
                try { File.Copy(PathOf, PathOf + ".bad", true); } catch { /* keep going without it */ }
                file = new RunSlots();
            }
        }

        // older save versions are upgraded here, one step at a time
        private static RunSlots Migrate(RunSaveFile f)
        {
            // version 1 is the first format; later versions convert old slots here before reading them
            for (int i = 0; f.titleClasses != null && f.titleNames != null && i < f.titleClasses.Length && i < f.titleNames.Length; i++)
                PerkRegistry.KnownTitles[f.titleClasses[i]] = f.titleNames[i];
            for (int i = 0; f.descClasses != null && f.descTexts != null && i < f.descClasses.Length && i < f.descTexts.Length; i++)
                PerkRegistry.KnownDescriptions[f.descClasses[i]] = f.descTexts[i];
            RunSnapshot Read(string json) => string.IsNullOrEmpty(json) ? new RunSnapshot() : JsonUtility.FromJson<RunSnapshot>(json) ?? new RunSnapshot();
            return new RunSlots { infinite = Read(f.infinite), classic = Read(f.classic) };
        }

        public static void Flush()
        {
            try
            {
                var tmp = PathOf + ".tmp";
                var onDisk = new RunSaveFile
                {
                    infinite = file.infinite.has ? JsonUtility.ToJson(file.infinite) : "",
                    classic = file.classic.has ? JsonUtility.ToJson(file.classic) : "",
                    titleClasses = PerkRegistry.KnownTitles.Keys.ToArray(),
                    titleNames = PerkRegistry.KnownTitles.Values.ToArray(),
                    descClasses = PerkRegistry.KnownDescriptions.Keys.ToArray(),
                    descTexts = PerkRegistry.KnownDescriptions.Values.ToArray(),
                };
                File.WriteAllText(tmp, JsonUtility.ToJson(onDisk, true));
                if (File.Exists(PathOf)) File.Delete(PathOf);
                File.Move(tmp, PathOf);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not write saved run: " + e.Message); }
        }

        private static bool InfiniteSlot => RunState.Mode == RunMode.Infinite;

        // runs worth saving: one player, past the tutorial
        private static bool Saving =>
            RunState.Mode != RunMode.Versus && GameManager.Instance != null && GameManager.Instance.numberOfPlayers == 1 && !TutorialManager.onTutorial && !TitleController.onTitle;

        public static void Clear(bool infinite)
        {
            if (infinite) file.infinite = new RunSnapshot(); else file.classic = new RunSnapshot();
            Flush();
            Plugin.Log.LogInfo($"Saved {(infinite ? "Infinite" : "Classic")} run cleared");
        }

        public static void ClearCurrent() { if (RunState.Mode != RunMode.Versus && GameManager.Instance != null && GameManager.Instance.numberOfPlayers == 1) Clear(InfiniteSlot); }

        // ------------------------------------------------------------ capture

        public static void SaveMatchStart(GameObject team, string opponentKey, IEnumerable<Technique> opponentPerks)
        {
            if (!Saving) return;
            var s = Capture("match");
            s.opponentKey = opponentKey;
            s.opponentName = team != null ? team.GetComponent<OpponentTeam>()?.teamName : null;
            s.opponentPerks = (opponentPerks ?? Enumerable.Empty<Technique>()).Select(Encode).Where(x => x != null).ToArray();
            Store(s);
        }

        public static void SaveUpgradeScreen()
        {
            if (!Saving) return;
            var last = InfiniteSlot ? file.infinite : file.classic;
            var s = Capture("upgrade");
            s.opponentKey = last.opponentKey;
            s.opponentName = last.opponentName;
            Store(s);
        }

        public static void SaveAfterPick()
        {
            if (!Saving) return;
            var s = Capture("next");
            s.gameNumber = GameManager.gameNumber + 1;     // the game adds 1 right after this
            Store(s);
        }

        private static void Store(RunSnapshot s)
        {
            if (InfiniteSlot) file.infinite = s; else file.classic = s;
            Flush();
            Plugin.Log.LogInfo($"Run saved ({(InfiniteSlot ? "Infinite" : "Classic")}, {s.stage}): {Summary(s)}");
        }

        private static RunSnapshot Capture(string stage)
        {
            var gm = GameManager.Instance;
            var ps = gm.playerStats;
            return new RunSnapshot
            {
                has = true,
                mode = RunState.Mode.ToString(),
                stage = stage,
                gameNumber = GameManager.gameNumber,
                infiniteMatch = RunState.InfiniteMatch,
                infiniteWins = RunState.InfiniteWins,
                loop = RunState.Loop,
                lineUp = TournamentLineUps.currentLineUp,
                runTimer = StatTracker.runTimer,
                statNames = ps.statUpgrades.Select(u => u.statName).ToArray(),
                statLevels = ps.statUpgrades.Select(u => u.currentLevel).ToArray(),
                techniques = ps.techniques.Select(Encode).Where(x => x != null).ToArray(),
                cardStats = StatCards.Points.Keys.ToArray(),
                cardPoints = StatCards.Points.Values.ToArray(),
                limits = new[] { LimitBreakEffects.limitSpike, LimitBreakEffects.limitBump, LimitBreakEffects.limitJump,
                                 LimitBreakEffects.limitSpinServe, LimitBreakEffects.limitFloatServe, LimitBreakEffects.limitBlock },
                statOverload = GameManager.statOverload,
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            };
        }

        private static string Encode(Technique t) =>
            t == null ? null : t is DataPerk d ? "perk:" + d.Def.Id : "game:" + t.GetType().Name;

        // ------------------------------------------------------------ restore

        public static string Summary(RunSnapshot s)
        {
            if (s == null || !s.has) return "";
            string where = s.mode == "Infinite" ? $"Round {s.infiniteMatch}" :
                           s.mode == "Loop" ? $"Loop {s.loop}, match {Math.Min(s.gameNumber, 8)}" :
                           s.gameNumber == 0 ? "Practice" : $"Match {s.gameNumber}";
            string vs = s.stage == "match" && !string.IsNullOrEmpty(s.opponentName) ? " vs " + s.opponentName.Replace("(Clone)", "").Trim() :
                        s.stage == "upgrade" ? " (picking an upgrade)" : "";
            return where + vs;
        }

        // short form for a menu button: "ROUND 7", "MATCH 3", "LOOP 2 · MATCH 1"
        public static string Where(RunSnapshot s) =>
            s == null || !s.has ? "" : s.mode == "Infinite" ? $"Round {s.infiniteMatch}" :
            s.mode == "Loop" ? $"Loop {s.loop} · Match {Math.Min(s.gameNumber, 8)}" : s.gameNumber == 0 ? "Practice" : $"Match {s.gameNumber}";

        public static int PerkCount(RunSnapshot s) => s?.techniques?.Length ?? 0;
        public static int BoostCount(RunSnapshot s) => (s?.statLevels?.Sum() ?? 0) + (s?.cardStats?.Length ?? 0);

        // Puts the saved state into the game. Returns false (and why) if something essential can't be restored.
        public static bool Apply(RunSnapshot s, out string problem)
        {
            problem = null;
            var gm = GameManager.Instance;
            var techs = new List<Technique>();
            var missing = new List<string>();
            foreach (var code in s.techniques)
            {
                var t = Decode(code);
                if (t != null) techs.Add(t); else missing.Add(code);
            }
            if (missing.Count > 0) Plugin.Log.LogWarning("Saved run: could not restore " + string.Join(", ", missing));

            RunState.Mode = (RunMode)Enum.Parse(typeof(RunMode), s.mode);
            RunState.InfiniteMatch = Math.Max(1, s.infiniteMatch);
            RunState.InfiniteWins = s.infiniteWins;
            RunState.Loop = Math.Max(1, s.loop);
            GameManager.gameNumber = s.gameNumber;
            TournamentLineUps.currentLineUp = s.lineUp;
            StatTracker.runTimer = s.runTimer;
            TutorialManager.onTutorial = false;
            gm.numberOfPlayers = 1;
            foreach (var u in gm.playerStats.statUpgrades)
            {
                int i = Array.IndexOf(s.statNames, u.statName);
                u.currentLevel = i >= 0 ? s.statLevels[i] : 0;
            }
            gm.playerStats.techniques.Clear();
            gm.playerStats.techniques.AddRange(techs);
            StatCards.ResetRun();
            for (int i = 0; i < s.cardStats.Length && i < s.cardPoints.Length; i++) StatCards.Points[s.cardStats[i]] = s.cardPoints[i];
            var l = s.limits != null && s.limits.Length == 6 ? s.limits : new bool[6];
            LimitBreakEffects.limitSpike = l[0]; LimitBreakEffects.limitBump = l[1]; LimitBreakEffects.limitJump = l[2];
            LimitBreakEffects.limitSpinServe = l[3]; LimitBreakEffects.limitFloatServe = l[4]; LimitBreakEffects.limitBlock = l[5];
            GameManager.statOverload = s.statOverload;

            TeamRoster.ResumeKey = s.stage == "match" ? s.opponentKey : null;
            OpponentScaling.ResumePerks = s.stage == "match" && s.mode != "Normal" ? s.opponentPerks.Select(Decode).Where(t => t != null).ToList() : null;
            if (missing.Count > 0) problem = $"{missing.Count} saved ability(s) could not be restored and were skipped.";
            Plugin.Log.LogInfo($"Run restored ({s.mode}, {s.stage}): {Summary(s)}, {techs.Count} abilities");
            return true;
        }

        private static Technique Decode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            if (code.StartsWith("perk:")) return PerkRegistry.Get(code.Substring(5));
            if (code.StartsWith("game:")) return PerkRegistry.FindVanilla(code.Substring(5));
            return null;
        }
    }
}

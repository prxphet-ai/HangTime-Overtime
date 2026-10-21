using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HangtimeOvertime.Engine;
using HangtimeOvertime.Generated;
using UnityEngine;

namespace HangtimeOvertime.Core
{
    // The rules a Versus match is played with (the lobby rules panel). Online the host's rules are the match's: they go to
    // every PC with the lobby screen and with each round load.
    [Serializable]
    public class RuleSet
    {
        public string Name = "STANDARD";
        public int Points = 5, Rounds = 5, Picks = 1, Offer = 3, StartPerks = 0;
        public bool Perks = true, Cards = true, DraftStart;
        public string MaxRarity = "epic";
        public string[] BannedElements = new string[0], BannedPerks = new string[0];

        public static RuleSet From(RulePresetDef d) => new RuleSet
        {
            Name = d.Name, Points = d.Points, Rounds = d.Rounds, Picks = d.Picks, Offer = d.Offer, StartPerks = d.StartPerks,
            Perks = d.Perks, Cards = d.Cards, DraftStart = d.DraftStart, MaxRarity = d.MaxRarity,
            BannedElements = d.BannedElements.ToArray(), BannedPerks = d.BannedPerks.ToArray(),
        };

        public RuleSet Copy() { var r = (RuleSet)MemberwiseClone(); r.BannedElements = BannedElements.ToArray(); r.BannedPerks = BannedPerks.ToArray(); return r; }

        public bool Upgrades => Picks > 0 && (Perks || Cards);
        public static int Rank(string rarity) => rarity == "epic" ? 2 : rarity == "rare" ? 1 : 0;

        // may this perk / card be offered (or given at the start)
        public bool Allows(Technique t)
        {
            if (!Perks || t == null) return false;
            string rarity = t is DataPerk d ? d.Def.Rarity : Scaling.VanillaRarity(t.GetType().Name);
            if (Rank(rarity) > Rank(MaxRarity)) return false;
            if (t is DataPerk p && (BannedElements.Contains(p.Def.Element) || BannedPerks.Contains(p.Def.Id))) return false;
            return true;
        }

        public bool Allows(StatCardDef c) => Cards && c != null && Rank(c.Rarity) <= Rank(MaxRarity);

        // "5 POINTS · FIRST TO 5 · 1 PICK OF 3 · PERKS + CARDS"
        public string Summary()
        {
            var parts = new List<string> { $"ROUNDS TO {Points} POINTS", $"FIRST TO {Rounds} ROUNDS" };
            parts.Add(!Upgrades ? "NO UPGRADES" : $"{Picks} {(Picks == 1 ? "PICK" : "PICKS")} OF {Offer} A ROUND ({(Perks && Cards ? "PERKS + CARDS" : Perks ? "PERKS" : "CARDS")}" +
                      (MaxRarity != "epic" ? $", UP TO {MaxRarity.ToUpperInvariant()}" : "") + ")");
            if (StartPerks > 0) parts.Add($"{StartPerks} STARTING {(StartPerks == 1 ? "PERK" : "PERKS")}");
            if (DraftStart) parts.Add("DRAFT START");
            int bans = BannedElements.Length + BannedPerks.Length;
            if (bans > 0) parts.Add($"{bans} {(bans == 1 ? "BAN" : "BANS")}");
            return string.Join("  ·  ", parts);
        }

        internal void Write(Online.NetWriter w)
        {
            w.S(Name).U8(Points).U8(Rounds).U8(Picks).U8(Offer).U8(StartPerks).B(Perks).B(Cards).B(DraftStart).S(MaxRarity);
            w.U8(BannedElements.Length); foreach (var e in BannedElements) w.S(e);
            w.U8(Math.Min(255, BannedPerks.Length)); foreach (var p in BannedPerks.Take(255)) w.S(p);
        }

        internal static RuleSet Read(Online.NetReader r)
        {
            var s = new RuleSet { Name = r.S(), Points = r.U8(), Rounds = r.U8(), Picks = r.U8(), Offer = r.U8(), StartPerks = r.U8(), Perks = r.B(), Cards = r.B(), DraftStart = r.B(), MaxRarity = r.S() };
            int n = r.U8(); s.BannedElements = Enumerable.Range(0, n).Select(_ => r.S()).ToArray();
            int m = r.U8(); s.BannedPerks = Enumerable.Range(0, m).Select(_ => r.S()).ToArray();
            s.Clamp();
            return s;
        }

        public void Clamp()
        {
            Points = Mathf.Clamp(Points, 1, 15); Rounds = Mathf.Clamp(Rounds, 1, 9); Picks = Mathf.Clamp(Picks, 0, 3);
            Offer = Mathf.Clamp(Offer, 2, 4); StartPerks = Mathf.Clamp(StartPerks, 0, 5);
            if (MaxRarity != "common" && MaxRarity != "rare") MaxRarity = "epic";
        }

        public bool SameAs(RuleSet o) => o != null && Points == o.Points && Rounds == o.Rounds && Picks == o.Picks && Offer == o.Offer && StartPerks == o.StartPerks &&
            Perks == o.Perks && Cards == o.Cards && DraftStart == o.DraftStart && MaxRarity == o.MaxRarity &&
            BannedElements.OrderBy(x => x).SequenceEqual(o.BannedElements.OrderBy(x => x)) && BannedPerks.OrderBy(x => x).SequenceEqual(o.BannedPerks.OrderBy(x => x));
    }

    // three custom presets (HangtimeOvertime_rules.json; one JSON object per slot, like the run saves)
    [Serializable]
    public class RulesFile
    {
        public int version = 1;
        public string[] custom = new string[3];
        public string last = "";                      // the rules last used (kept between sessions)
    }

    internal static class LobbyRules
    {
        public static RuleSet Current = RuleSet.From(RulePresets.All[0]);
        public const int CustomSlots = 3;
        private static RulesFile file;
        private static string PathOf => Path.Combine(Application.persistentDataPath, "HangtimeOvertime_rules.json");

        private static RulesFile File_
        {
            get
            {
                if (file != null) return file;
                try { file = File.Exists(PathOf) ? JsonUtility.FromJson<RulesFile>(File.ReadAllText(PathOf)) : null; }
                catch (Exception e) { Plugin.Log.LogWarning("Rules file unreadable: " + e.Message); }
                if (file == null) file = new RulesFile();
                if (file.custom == null || file.custom.Length != CustomSlots) Array.Resize(ref file.custom, CustomSlots);
                return file;
            }
        }

        private static void Save()
        {
            try
            {
                var tmp = PathOf + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(File_, true));
                if (File.Exists(PathOf)) File.Delete(PathOf);
                File.Move(tmp, PathOf);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't save rules: " + e.Message); }
        }

        // the rules used last time (a new session starts with them)
        public static void LoadLast()
        {
            var j = File_.last;
            if (string.IsNullOrEmpty(j)) return;
            try { var r = JsonUtility.FromJson<RuleSet>(j); if (r != null) { r.Clamp(); Current = r; } } catch { }
        }

        // a friend's PC leaves a session: its own rules again (the host's were only for that session)
        public static void Restore() { Current = RuleSet.From(RulePresets.All[0]); LoadLast(); }

        public static void Changed()
        {
            Current.Clamp();
            File_.last = JsonUtility.ToJson(Current);
            Save();
        }

        public static RuleSet Custom(int slot)
        {
            var j = File_.custom[slot];
            if (string.IsNullOrEmpty(j)) return null;
            try { var r = JsonUtility.FromJson<RuleSet>(j); r?.Clamp(); return r; } catch { return null; }
        }

        public static void SaveCustom(int slot)
        {
            var r = Current.Copy();
            r.Name = $"CUSTOM {slot + 1}";
            File_.custom[slot] = JsonUtility.ToJson(r);
            Current.Name = r.Name;
            Changed();
            Plugin.Log.LogInfo($"Rules saved to custom {slot + 1}: {r.Summary()}");
        }

        // which preset the current rules match (built-in or custom), or null
        public static string MatchingPreset()
        {
            foreach (var d in RulePresets.All) if (Current.SameAs(RuleSet.From(d))) return d.Name;
            for (int i = 0; i < CustomSlots; i++) if (Current.SameAs(Custom(i))) return $"CUSTOM {i + 1}";
            return null;
        }

        // the match starts: each side's random starting perks (seeded, so the same on every PC that sees the seed)
        public static void GiveStartingPerks(int seed)
        {
            if (Current.StartPerks <= 0) return;
            var rng = new System.Random(seed);
            for (int side = 0; side < 2; side++)
            {
                var have = VersusState.PerksOf(side);
                var pool = PerkRegistry.All.Cast<Technique>().Where(t => Current.Allows(t) && !have.Contains(t)).OrderBy(t => ((DataPerk)t).Def.Id).ToList();
                for (int i = 0; i < Current.StartPerks && pool.Count > 0; i++)
                {
                    int k = rng.Next(pool.Count);
                    have.Add(pool[k]);
                    pool.RemoveAt(k);
                }
                Plugin.Log.LogInfo($"Rules: {VersusState.Setup.SideLabel(side)} starts with {string.Join(", ", have.OfType<DataPerk>().Select(p => p.Def.Title))}");
            }
        }
    }
}

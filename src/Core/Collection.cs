using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HangtimeOvertime.Engine;
using HangtimeOvertime.Generated;
using UnityEngine;

namespace HangtimeOvertime.Core
{
    // What the player has come across (HangtimeOvertime_collection.json next to the game's save, never the save itself):
    // teams faced and beaten, perks and stat cards offered and picked. Keys: "team:<name>", "perk:<id>", "vperk:<game
    // class>", "card:<id>", "vcard:<game card title>". The game's own cards are also catalogued (title, text) the first
    // time an upgrade screen holds them, so the collection can list them as ??? before they're found.
    [Serializable]
    public class CollectionData
    {
        public int version = 1;
        public string[] keys = new string[0];
        public int[] seen = new int[0], picked = new int[0], beaten = new int[0];
        public string[] vperkClass = new string[0], vperkTitle = new string[0], vperkText = new string[0];
        public string[] vcardTitle = new string[0], vcardText = new string[0];
    }

    // one row of the collection screen
    internal class CollectionEntry
    {
        public string Key, Title, Group, Element, Text, Flavour, Kind;   // Group: rarity (perks, cards) or slot class (teams)
        public string Style = "";                                        // stat cards: boost / playstyle / tradeoff / game
        public List<string> Lines = new List<string>();
        public int Seen, Picked, Beaten;
        public bool Found => Seen > 0 || Picked > 0;
    }

    internal static class Collection
    {
        private static CollectionData data;
        private static readonly Dictionary<string, int> index = new Dictionary<string, int>();
        private static string PathOf => Path.Combine(Application.persistentDataPath, "HangtimeOvertime_collection.json");
        public static CollectionData Data { get { Load(); return data; } }

        public static void Load()
        {
            if (data != null) return;
            try { data = File.Exists(PathOf) ? JsonUtility.FromJson<CollectionData>(File.ReadAllText(PathOf)) : null; }
            catch (Exception e) { Plugin.Log.LogWarning("Collection file unreadable, starting fresh: " + e.Message); }
            if (data == null) data = new CollectionData();
            int n = data.keys.Length;
            Array.Resize(ref data.seen, n); Array.Resize(ref data.picked, n); Array.Resize(ref data.beaten, n);
            int v = data.vperkClass.Length; Array.Resize(ref data.vperkTitle, v); Array.Resize(ref data.vperkText, v);
            int c = data.vcardTitle.Length; Array.Resize(ref data.vcardText, c);
            index.Clear();
            for (int i = 0; i < n; i++) index[data.keys[i]] = i;
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
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't save the collection: " + e.Message); }
        }

        private static int Index(string key)
        {
            var d = Data;
            if (index.TryGetValue(key, out var i)) return i;
            int n = d.keys.Length + 1;
            Array.Resize(ref d.keys, n); Array.Resize(ref d.seen, n); Array.Resize(ref d.picked, n); Array.Resize(ref d.beaten, n);
            d.keys[n - 1] = key;
            index[key] = n - 1;
            return n - 1;
        }

        public static int Seen(string key) { Load(); return index.TryGetValue(key, out var i) ? data.seen[i] : 0; }
        public static int Picked(string key) { Load(); return index.TryGetValue(key, out var i) ? data.picked[i] : 0; }
        public static int Beaten(string key) { Load(); return index.TryGetValue(key, out var i) ? data.beaten[i] : 0; }

        public static void See(string key)
        {
            if (key == null) return;
            int i = Index(key);
            if (data.seen[i] == 0) Plugin.Log.LogInfo("Collection: discovered " + key);
            data.seen[i]++;
            Save();
        }

        public static void Pick(string key) { if (key == null) return; int i = Index(key); data.picked[i]++; if (data.seen[i] == 0) data.seen[i] = 1; Save(); }
        public static void Beat(string key) { if (key == null) return; int i = Index(key); data.beaten[i]++; Save(); }

        // ------------------------------------------------------------ keys

        public static string KeyOf(Technique t) => t == null ? null : t is DataPerk d ? "perk:" + d.Def.Id : "vperk:" + t.GetType().Name;
        public static string KeyOf(UpgradePair p)
        {
            if (p == null) return null;
            var c = Engine.StatCards.CardOf(p);
            return c != null ? "card:" + c.Id : string.IsNullOrEmpty(p.title) ? null : "vcard:" + p.title;
        }
        public static string TeamKey(string name) => string.IsNullOrEmpty(name) ? null : "team:" + name.Trim();

        // ------------------------------------------------------------ the game's own cards

        public static void CatalogPerk(Technique t, string title, string text)
        {
            if (t == null || t is DataPerk || string.IsNullOrEmpty(title)) return;
            var d = Data;
            string cls = t.GetType().Name;
            int i = Array.IndexOf(d.vperkClass, cls);
            if (i >= 0 && d.vperkTitle[i] == title && d.vperkText[i] == text) return;
            if (i < 0)
            {
                i = d.vperkClass.Length;
                Array.Resize(ref d.vperkClass, i + 1); Array.Resize(ref d.vperkTitle, i + 1); Array.Resize(ref d.vperkText, i + 1);
                d.vperkClass[i] = cls;
            }
            d.vperkTitle[i] = title; d.vperkText[i] = text ?? "";
            Save();
        }

        public static void CatalogCard(UpgradePair p)
        {
            if (p == null || Engine.StatCards.CardOf(p) != null || string.IsNullOrEmpty(p.title)) return;
            var d = Data;
            int i = Array.IndexOf(d.vcardTitle, p.title);
            if (i >= 0 && d.vcardText[i] == (p.description ?? "")) return;
            if (i < 0) { i = d.vcardTitle.Length; Array.Resize(ref d.vcardTitle, i + 1); Array.Resize(ref d.vcardText, i + 1); d.vcardTitle[i] = p.title; }
            d.vcardText[i] = p.description ?? "";
            Save();
        }

        // ------------------------------------------------------------ the screen's rows

        private static string Spaced(string cls) => Regex.Replace(cls, "(?<=[a-z])(?=[A-Z])", " ");

        private static CollectionEntry Fill(CollectionEntry e)
        {
            e.Seen = Seen(e.Key); e.Picked = Picked(e.Key); e.Beaten = Beaten(e.Key);
            return e;
        }

        private static string PerkTitle(string perkId) =>
            Perks.All.FirstOrDefault(p => p.Id == perkId)?.Title ?? perkId;

        public static List<CollectionEntry> TeamEntries()
        {
            var list = new List<CollectionEntry>();
            foreach (var t in Teams.All)
            {
                var e = new CollectionEntry { Key = TeamKey(t.Name), Title = t.Name, Group = t.Slot, Element = t.Element, Kind = "team", Text = $"\"{t.Intro}\"", Flavour = t.Comment };
                e.Lines.Add($"SIGNATURE: {string.Join(", ", t.Signature.Select(PerkTitle)).ToUpperInvariant()}");
                list.Add(Fill(e));
            }
            void Vanilla(string[] names, string slot)
            {
                foreach (var n in names)
                {
                    var e = new CollectionEntry { Key = TeamKey(n), Title = n, Group = slot, Element = Teams.VanillaElement(n), Kind = "team", Text = "ONE OF THE GAME'S OWN TEAMS.", Flavour = "" };
                    list.Add(Fill(e));
                }
            }
            Vanilla(Teams.VanillaRegular, "regular"); Vanilla(Teams.VanillaCombo, "combo"); Vanilla(Teams.VanillaBoss, "boss");
            return list;
        }

        public static List<CollectionEntry> PerkEntries()
        {
            var list = new List<CollectionEntry>();
            foreach (var p in Perks.All)
                list.Add(Fill(new CollectionEntry { Key = "perk:" + p.Id, Title = p.Title, Group = p.Rarity, Element = p.Element, Kind = "perk", Text = p.Description, Flavour = p.Flavour }));
            var d = Data;
            foreach (var cls in Scaling.VanillaTechniques)
            {
                int i = Array.IndexOf(d.vperkClass, cls);
                string title = i >= 0 ? d.vperkTitle[i] : PerkRegistry.KnownTitles.TryGetValue(cls, out var kt) ? kt : Spaced(cls);
                string text = i >= 0 ? d.vperkText[i] : PerkRegistry.KnownDescriptions.TryGetValue(cls, out var kd) ? kd : "";
                var e = new CollectionEntry { Key = "vperk:" + cls, Title = title, Group = Scaling.VanillaRarity(cls), Element = "none", Kind = "perk", Text = text, Flavour = "" };
                e.Lines.Add("ONE OF THE GAME'S OWN TECHNIQUES");
                list.Add(Fill(e));
            }
            return list;
        }

        public static List<CollectionEntry> CardEntries()
        {
            var list = new List<CollectionEntry>();
            foreach (var c in Generated.StatCards.All)
            {
                var e = new CollectionEntry { Key = "card:" + c.Id, Title = c.Title, Group = c.Rarity, Element = "none", Style = c.Style, Kind = "card", Text = c.Blurb, Flavour = "" };
                for (int i = 0; i < c.Stats.Length; i++) e.Lines.Add($"{Engine.StatCards.Label(c.Stats[i]).ToUpperInvariant()} {(c.Points[i] > 0 ? "+" : "")}{c.Points[i]:0.##}");
                list.Add(Fill(e));
            }
            var d = Data;
            for (int i = 0; i < d.vcardTitle.Length; i++)
            {
                var e = new CollectionEntry { Key = "vcard:" + d.vcardTitle[i], Title = d.vcardTitle[i], Group = "game", Element = "none", Style = "game", Kind = "card", Text = d.vcardText[i], Flavour = "" };
                e.Lines.Add("ONE OF THE GAME'S OWN STAT CARDS (UP TO LEVEL 3)");
                list.Add(Fill(e));
            }
            return list;
        }
    }
}

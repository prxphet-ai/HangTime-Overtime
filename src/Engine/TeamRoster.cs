using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Generated;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Engine
{
    // Opponents for Infinite and Loop runs: a random team for the bracket slot's pool (the game's
    // own teams plus new ones), new teams dressed up from a vanilla team when they spawn, and
    // random perks for whoever you face.
    internal static class TeamRoster
    {
        private static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        private static readonly Queue<string> recent = new Queue<string>();
        private static readonly Dictionary<string, Sprite> banners = new Dictionary<string, Sprite>();

        public static GameObject ForcedPrefab;   // what the game spawns this match (lineup slots)
        public static TeamDef Current;           // the new team being played, or null
        private static string currentKey;

        private static void CollectPrefabs()
        {
            var gm = GameManager.Instance;
            void Add(GameObject g) { if (g != null && !prefabs.ContainsKey(g.name)) prefabs[g.name] = g; }
            foreach (var t in gm.teams) Add(t);
            foreach (var t in gm.comboTeams) Add(t);
            Add(gm.midBoss1); Add(gm.midBoss2); Add(gm.finalBoss);
            var lineUps = Traverse.Create(gm.tournamentLineUps).Field("lineUps").GetValue<List<LineUp>>();
            if (lineUps != null) foreach (var l in lineUps) foreach (var t in l.lineUp) Add(t);
        }

        // Picks the opponent for this slot; returns the prefab the game should spawn.
        public static string DevForce;   // development: next opponent (new-team id)
        public static string ResumeKey;  // a resumed run: the saved opponent (new-team id or the game's prefab name), used once
        public static string CurrentKey => currentKey;

        public static GameObject Choose(int slot)
        {
            if (prefabs.Count == 0) CollectPrefabs();
            if (ResumeKey != null)
            {
                string key = ResumeKey;
                ResumeKey = null;
                var saved = Generated.Teams.All.FirstOrDefault(t => t.Id == key);
                if (saved != null && prefabs.ContainsKey(saved.Base)) { Current = saved; currentKey = saved.Id; return prefabs[saved.Base]; }
                if (prefabs.ContainsKey(key)) { Current = null; currentKey = key; return prefabs[key]; }
                Plugin.Log.LogWarning("Saved opponent " + key + " not found; picking a new one");
            }
            if (DevForce != null)
            {
                var forced = Generated.Teams.All.FirstOrDefault(t => t.Id == DevForce);
                if (forced != null && prefabs.ContainsKey(forced.Base)) { Current = forced; currentKey = forced.Id; return prefabs[forced.Base]; }
            }
            string cls = slot >= 1 && slot <= 8 ? Generated.Teams.SlotClass[slot] : "regular";
            var vanilla = cls == "boss" ? Generated.Teams.VanillaBoss : cls == "combo" ? Generated.Teams.VanillaCombo : Generated.Teams.VanillaRegular;
            var pool = vanilla.Where(prefabs.ContainsKey).Select(n => (key: n, def: (TeamDef)null))
                .Concat(Generated.Teams.All.Where(t => t.Slot == cls && prefabs.ContainsKey(t.Base)).Select(t => (key: t.Id, def: t)))
                .ToList();
            var fresh = pool.Where(p => !recent.Contains(p.key)).ToList();
            var pick = (fresh.Count > 0 ? fresh : pool)[Random.Range(0, (fresh.Count > 0 ? fresh : pool).Count)];
            recent.Enqueue(pick.key);
            while (recent.Count > 3) recent.Dequeue();
            Current = pick.def;
            currentKey = pick.key;
            Plugin.Log.LogInfo($"Opponent for slot {slot} ({cls}): {(pick.def != null ? pick.def.Name + " (new, on " + pick.def.Base + ")" : pick.key)} from {pool.Count} teams");
            return prefabs[pick.def != null ? pick.def.Base : pick.key];
        }

        // a resumed upgrade screen: which new team (if any) was just beaten, for its comment line
        public static void RestoreCurrent(string key)
        {
            Current = Generated.Teams.All.FirstOrDefault(t => t.Id == key);
            currentKey = key;
        }

        public static void ClearChoice() { ForcedPrefab = null; Current = null; currentKey = null; }

        // ------------------------------------------------------------ dressing a new team

        public static void Dress(GameObject team, TeamDef def, int slot)
        {
            var ot = team.GetComponent<OpponentTeam>();
            var stats = team.GetComponent<PlayerStats>();
            team.name = def.Name + "(Clone)";
            ot.banner = Banner(ot.banner, def);
            ot.teamName = def.Name;
            ot.introDialogue = def.Intro;
            ot.winDialogue = def.Win;
            ot.loseDialogue = def.Lose;
            ot.gotPointDialogue = new List<string>(def.GotPoint) { Capacity = def.GotPoint.Length };
            ot.lostPointDialogue = new List<string>(def.LostPoint) { Capacity = def.LostPoint.Length };
            ot.talkPitch = def.Pitch;
            var weights = new List<StatWeight>();
            for (int i = 0; i < def.WeightStats.Length; i++) weights.Add(new StatWeight { statName = def.WeightStats[i], weight = def.Weights[i] });
            weights.Capacity = weights.Count;
            Traverse.Create(ot).Field("statWeights").SetValue(weights);   // OpponentScaling levels the team with these

            int idx = 0;
            foreach (var pc in team.GetComponentsInChildren<PlayerController>(true))
            {
                var hair = Vfx.Hex(def.Hair[Mathf.Min(idx, def.Hair.Length - 1)]);
                foreach (var sr in pc.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (sr.name == "Core_0") sr.color = Vfx.Hex(def.Jersey);
                    else if (sr.name.StartsWith("Thigh_0")) sr.color = Vfx.Hex(def.Shorts);
                    else if (sr.name == "Hair_0" || sr.name == "Front hair") sr.color = hair;
                }
                pc.GetComponent<ControllerFx>()?.RefreshColors();
                idx++;
            }
        }

        // The team's banner emblem: assets/emblems/<id>.png (drawn by tools/emblems.py in the game's emblem style),
        // shipped in the plugin's emblems folder; sized to the vanilla emblem it replaces.
        private static Sprite Banner(Sprite baseBanner, TeamDef def)
        {
            if (banners.TryGetValue(def.Id, out var cached) && cached != null) return cached;
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "emblems", def.Id + ".png");
            if (!System.IO.File.Exists(path)) { Plugin.Log.LogWarning("No emblem " + path); return baseBanner; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontUnloadUnusedAsset, wrapMode = TextureWrapMode.Clamp };
            tex.LoadImage(System.IO.File.ReadAllBytes(path));
            float fit = baseBanner != null ? Mathf.Max(tex.width / baseBanner.bounds.size.x, tex.height / baseBanner.bounds.size.y) : 100f;
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), fit);
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            sprite.name = def.Name + " emblem";
            banners[def.Id] = sprite;
            return sprite;
        }

        public static void LabelBanner(SpriteRenderer banner)
        {
            if (banner == null || Current == null) return;
            var team = GameManager.Instance.opponentTeam;
            if (team != null && team.banner != null && banner.sprite != team.banner) banner.sprite = team.banner;
            // the emblem hangs on a cloth sprite: give the cloth the team's banner color
            var cloth = banner.transform.parent != null ? banner.transform.parent.GetComponent<SpriteRenderer>() : null;
            if (cloth != null)
            {
                // a darkened team color keeps the light emblem readable under the gym lighting, like the game's own banners
                var c = Color.Lerp(Vfx.Hex(Current.Banner), Color.black, 0.42f);
                cloth.color = new Color(c.r, c.g, c.b, cloth.color.a);
                // fit the emblem inside the cloth (the emblem is sized to the old emblem first)
                float fit = Mathf.Min(0.78f * cloth.bounds.size.x / Mathf.Max(0.01f, banner.bounds.size.x),
                                      0.62f * cloth.bounds.size.y / Mathf.Max(0.01f, banner.bounds.size.y));
                if (fit < 1f) banner.transform.localScale *= fit;
            }
            Plugin.Trace($"banner emblem {banner.sprite?.name} on {cloth?.name}");
        }

        // ------------------------------------------------------------ opponent perks

        private static readonly AccessTools.FieldRef<BallMovement, OpponentDialogue> fDialogue = AccessTools.FieldRefAccess<BallMovement, OpponentDialogue>("opponentDialogue");

        public static void Announce(GameObject team, string names)
        {
            var ball = GameManager.Instance.ball != null ? GameManager.Instance.ball.GetComponent<BallMovement>() : null;
            var dlg = ball != null ? fDialogue(ball) : null;
            var ot = team.GetComponent<OpponentTeam>();
            if (dlg != null && ot != null) dlg.StartCoroutine(dlg.DelayedDialogue("Our abilities:\n" + names + "!", 3.2f, ot.talkPitch));
        }
    }
}

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
        public static GameObject Choose(int slot)
        {
            if (prefabs.Count == 0) CollectPrefabs();
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

        public static void ClearChoice() { ForcedPrefab = null; Current = null; currentKey = null; }

        public static int SkillPoints(int slot) => slot * 3 + 1 + (slot == 3 ? 2 : slot == 8 ? -5 : 0);

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
            Traverse.Create(ot).Field("statWeights").SetValue(weights);
            foreach (var s in stats.statUpgrades) s.currentLevel = 0;
            ot.Init(SkillPoints(slot));

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

        // The banner's team emblem for a new team: a disc in the team color (the name is drawn on it in game text).
        private static Sprite Banner(Sprite baseBanner, TeamDef def)
        {
            if (banners.TryGetValue(def.Id, out var cached) && cached != null) return cached;
            const int size = 256;
            Color fill = Vfx.Hex(def.Banner), edge = Color.Lerp(fill, Color.black, 0.45f);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f, d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color c = d > 0.86f ? edge : Color.Lerp(fill, fill * 0.8f, (dy + 1f) / 2f);
                    c.a = Mathf.Clamp01((1f - d) / 0.03f);   // soft rim, transparent outside
                    px[y * size + x] = c;
                }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontUnloadUnusedAsset, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply();
            float ppu = baseBanner != null ? size / (0.72f * Mathf.Max(baseBanner.bounds.size.x, baseBanner.bounds.size.y)) : 100f;
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), ppu);
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            sprite.name = def.Name + " emblem";
            banners[def.Id] = sprite;
            return sprite;
        }

        public static void LabelBanner(SpriteRenderer banner)
        {
            if (banner == null) return;
            var old = banner.transform.Find("Overtime team name");
            if (old != null) Object.Destroy(old.gameObject);
            if (Current == null || banner.sprite == null) return;
            var team = GameManager.Instance.opponentTeam;
            if (team != null && team.banner != null && banner.sprite != team.banner) banner.sprite = team.banner;
            Plugin.Trace($"banner '{banner.name}' sprite={banner.sprite.name} parent={banner.transform.parent?.name} siblings=[{string.Join(", ", banner.transform.parent != null ? banner.transform.parent.GetComponentsInChildren<SpriteRenderer>(true).Select(s => s.name + ":" + (s.sprite ? s.sprite.name : "-")) : new string[0])}]");
            var font = Object.FindObjectsByType<TextMeshPro>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(t => t.font).FirstOrDefault(f => f != null);
            var go = new GameObject("Overtime team name");
            go.transform.SetParent(banner.transform, false);
            var tmp = go.AddComponent<TextMeshPro>();
            if (font != null) tmp.font = font;
            var bounds = banner.sprite.bounds;
            go.transform.localPosition = bounds.center + new Vector3(0f, bounds.size.y * 0.08f, -0.01f);
            tmp.rectTransform.sizeDelta = new Vector2(bounds.size.x * 0.8f, bounds.size.y * 0.55f);
            tmp.text = Current.Name.ToUpperInvariant().Replace(" ", "\n");
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.1f;
            tmp.fontSizeMax = 40f;
            tmp.color = Color.white;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sortingLayerID = banner.sortingLayerID;
            mr.sortingOrder = banner.sortingOrder + 1;
        }

        // ------------------------------------------------------------ opponent perks

        public static void GivePerks(GameObject team, int slot)
        {
            var stats = team.GetComponent<PlayerStats>();
            if (stats == null) return;
            bool boss = slot >= 1 && slot <= 8 && Generated.Teams.SlotClass[slot] == "boss";
            int count = RunState.Mode == RunMode.Infinite ? RunState.InfiniteMatch / Generated.Teams.PerksPerInfiniteMatches
                      : RunState.Mode == RunMode.Loop ? (RunState.Loop - 1) * Generated.Teams.PerksPerLoop : 0;
            if (count > 0 && boss) count += Generated.Teams.BossBonus;
            var given = new List<Technique>();
            if (Current != null)
                foreach (var id in Current.Signature) { var p = PerkRegistry.Get(id); if (p != null) given.Add(p); }
            count = Mathf.Min(Mathf.Max(count, given.Count), Generated.Teams.MaxPerks);
            var pool = PerkRegistry.All.Where(p => p.Def.OpponentOk).Cast<Technique>()
                .Concat(Scaling.OpponentTechniques.Select(PerkRegistry.FindVanilla).Where(t => t != null))
                .Where(t => !given.Contains(t)).OrderBy(_ => Random.value).ToList();
            foreach (var t in pool) { if (given.Count >= count) break; given.Add(t); }
            foreach (var t in given) if (!stats.techniques.Contains(t)) stats.techniques.Add(t);
            if (given.Count == 0) return;
            string names = string.Join(", ", given.Select(t => t is DataPerk d ? d.Def.Title : PerkRegistry.VanillaTitle(t)));
            Plugin.Log.LogInfo($"Opponent perks: {names}");
            Announce(team, names);
        }

        private static readonly AccessTools.FieldRef<BallMovement, OpponentDialogue> fDialogue = AccessTools.FieldRefAccess<BallMovement, OpponentDialogue>("opponentDialogue");

        private static void Announce(GameObject team, string names)
        {
            var ball = GameManager.Instance.ball != null ? GameManager.Instance.ball.GetComponent<BallMovement>() : null;
            var dlg = ball != null ? fDialogue(ball) : null;
            var ot = team.GetComponent<OpponentTeam>();
            if (dlg != null && ot != null) dlg.StartCoroutine(dlg.DelayedDialogue("Our abilities: " + names + "!", 3.2f, ot.talkPitch));
        }
    }
}

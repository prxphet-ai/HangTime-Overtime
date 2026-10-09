using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace HangtimeOvertime.Debugging
{
    // Development aid (DumpScenes): writes the numbers the match simulator (tools/sim.py) is built on,
    // read from the player's own install: every team's stat tables, weights and AI settings, the
    // player team, and the ball's constants. Output: BepInEx/plugins/HangtimeOvertime/sim_data/game_data.json
    internal static class SimExport
    {
        private static bool done;

        public static void Write()
        {
            var gm = GameManager.Instance;
            if (done || gm == null) return;
            var ball = Object.FindObjectsByType<BallMovement>(FindObjectsSortMode.None).FirstOrDefault();
            var playerTeam = Object.FindObjectsByType<SetTeamStat>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            if (ball == null || playerTeam == null) return;
            done = true;

            var teams = new Dictionary<string, GameObject>();
            void Add(GameObject g) { if (g != null && !teams.ContainsKey(g.name)) teams[g.name] = g; }
            foreach (var t in gm.teams) Add(t);
            foreach (var t in gm.comboTeams) Add(t);
            Add(gm.practiceTeam); Add(gm.midBoss1); Add(gm.midBoss2); Add(gm.finalBoss);
            var lineUps = Traverse.Create(gm.tournamentLineUps).Field("lineUps").GetValue<List<LineUp>>();
            if (lineUps != null) foreach (var l in lineUps) foreach (var t in l.lineUp) Add(t);

            var sb = new StringBuilder("{\n");
            sb.Append("  \"source\": \"Hangtime! build exported by HangtimeOvertime SimExport\",\n");
            sb.Append($"  \"gravity\": {F(Physics2D.gravity.y)},\n");
            sb.Append("  \"ball\": ").Append(Fields(ball, ball.GetComponent<Rigidbody2D>())).Append(",\n");
            sb.Append($"  \"matchLength\": {gm.matchLength},\n");
            sb.Append("  \"player_team\": ").Append(Team(playerTeam.gameObject, gm.playerStats)).Append(",\n");
            sb.Append("  \"teams\": {\n");
            sb.Append(string.Join(",\n", teams.Select(kv => $"    \"{kv.Key}\": {Team(kv.Value, kv.Value.GetComponent<PlayerStats>())}")));
            sb.Append("\n  },\n");
            sb.Append("  \"lineups\": [").Append(string.Join(", ", (lineUps ?? new List<LineUp>()).Select(l => "[" + string.Join(", ", l.lineUp.Select(x => x ? $"\"{x.name}\"" : "null")) + "]"))).Append("],\n");
            sb.Append($"  \"bosses\": {{\"midBoss1\": \"{gm.midBoss1?.name}\", \"midBoss2\": \"{gm.midBoss2?.name}\", \"finalBoss\": \"{gm.finalBoss?.name}\", \"practice\": \"{gm.practiceTeam?.name}\"}},\n");
            // the game's scaling formulas for a parity check against tools/simlib.py (tools/sim_check.py)
            var rows = new List<string>();
            foreach (var cls in new[] { "regular", "combo", "boss" })
                for (int r = 1; r <= 20; r++)
                    foreach (var pw in new[] { 0f, 5f, 15f })
                        rows.Add($"[\"{cls}\", {r}, {F(pw)}, {F(Engine.OpponentScaling.Points(r, cls, pw))}, {F(Engine.OpponentScaling.StatMult(r))}, {F(Engine.OpponentScaling.MoveMult(r))}, {Engine.OpponentScaling.PerkCount(r, cls)}]");
            sb.Append("  \"scaling_table\": [").Append(string.Join(", ", rows)).Append("],\n");
            sb.Append("  \"combo\": [").Append(string.Join(", ", gm.comboTeams.Select(c => $"\"{c?.name}\""))).Append("]\n");
            sb.Append("}\n");

            var dir = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "sim_data");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "game_data.json"), sb.ToString());

            // reference images of the game's team emblems (for drawing new ones in the same style; never shipped)
            var emb = Path.Combine(dir, "emblems");
            Directory.CreateDirectory(emb);
            foreach (var kv in teams)
            {
                var ot = kv.Value.GetComponent<OpponentTeam>();
                if (ot != null && ot.banner != null) SavePng(ot.banner, Path.Combine(emb, kv.Key + ".png"));
            }
            foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (sr.sprite != null && (sr.name.Contains("Banner") || sr.name.Contains("banner") || sr.sprite.name.ToLower().Contains("banner")))
                    SavePng(sr.sprite, Path.Combine(emb, "scene_" + sr.name.Replace(" ", "_") + "_" + sr.sprite.name.Replace(" ", "_") + ".png"));
            Plugin.Log.LogInfo("Sim data written to " + dir);
        }

        private static void SavePng(Sprite s, string path)
        {
            try
            {
                var r = s.textureRect;
                var rt = RenderTexture.GetTemporary(s.texture.width, s.texture.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(s.texture, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.Destroy(tex);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"emblem export {s.name}: {e.Message}"); }
        }

        private static string F(float f) => float.IsNaN(f) || float.IsInfinity(f) ? "0" : f.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

        private static string Team(GameObject team, PlayerStats stats)
        {
            var ot = team.GetComponent<OpponentTeam>();
            var parts = new List<string>();
            if (ot != null)
            {
                parts.Add($"\"teamName\": \"{ot.teamName}\"");
                var w = Traverse.Create(ot).Field("statWeights").GetValue<List<StatWeight>>();
                parts.Add("\"weights\": {" + string.Join(", ", w.Select(x => $"\"{x.statName}\": {F(x.weight)}")) + "}");
            }
            if (stats != null)
            {
                parts.Add("\"levels\": {" + string.Join(", ", stats.statUpgrades.Select(s => $"\"{s.statName}\": [{string.Join(", ", s.levels.Select(F))}]")) + "}");
                parts.Add("\"techniques\": [" + string.Join(", ", stats.techniques.Select(t => $"\"{t.GetType().Name}\"")) + "]");
            }
            var players = team.GetComponentsInChildren<PlayerController>(true).Select(pc =>
            {
                var comps = new List<string> { "\"name\": \"" + pc.name + "\"", "\"controller\": " + Fields(pc) };
                foreach (var ai in pc.GetComponents<MonoBehaviour>().Where(m => m is SpikerInput || m is SetterInput))
                    comps.Add($"\"{ai.GetType().Name}\": " + Fields(ai));
                var ab = pc.GetComponent<AbilityBooleans>();
                if (ab != null) comps.Add("\"abilities\": " + Fields(ab));
                return "{" + string.Join(", ", comps) + "}";
            });
            parts.Add("\"players\": [" + string.Join(", ", players) + "]");
            return "{" + string.Join(", ", parts) + "}";
        }

        // Plain numeric/bool/vector fields of a component (serialized settings), by reflection.
        private static string Fields(Object obj, Rigidbody2D rb = null)
        {
            var parts = new List<string>();
            foreach (var f in obj.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object v = f.GetValue(obj);
                switch (v)
                {
                    case float x: parts.Add($"\"{f.Name}\": {F(x)}"); break;
                    case int x: parts.Add($"\"{f.Name}\": {x}"); break;
                    case bool x: parts.Add($"\"{f.Name}\": {(x ? "true" : "false")}"); break;
                    case Vector2 x: parts.Add($"\"{f.Name}\": [{F(x.x)}, {F(x.y)}]"); break;
                }
            }
            if (rb != null) parts.Add($"\"rb_gravityScale\": {F(rb.gravityScale)}, \"rb_drag\": {F(rb.linearDamping)}, \"rb_mass\": {F(rb.mass)}");
            return "{" + string.Join(", ", parts) + "}";
        }
    }
}

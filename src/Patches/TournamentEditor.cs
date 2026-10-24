using System;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HangtimeOvertime.Generated;
using TMPro;
using UnityEngine;
using Rules = HangtimeOvertime.Generated.Community;
using StatCards = HangtimeOvertime.Engine.StatCards;

namespace HangtimeOvertime.Patches
{
    // The tournament creator. TOURNAMENT page: name, description, FIXED / DRAFT, how many matches, the match list, YOUR TEAM
    // (FIXED), the simulator's difficulty; SAVE, TEST PLAY, COPY CODE. A match's page: the team's name and line, squad (body
    // and AI), colours, emblem (mascot, layout, lettering, accent, or COPY LOOK from one of the mod's teams), stat levels
    // 0-3, perks and stat cards (only the mod's own). YOUR TEAM: the player's levels, perks and cards for FIXED.
    // Everything is kept in a working copy; SAVE (and leaving) stores it, which makes a new version: a changed tournament
    // has to be beaten again before it can be published.
    internal class TournamentEditor : OvertimePanel
    {
        private static TournamentEditor open;
        public static bool IsOpen => open != null;
        private TourEntry entry;
        private Tournament work;
        private int pageNo = -2;                          // -2 tournament, -1 your team, 0.. a match
        private Field nameField, descField;
        private string status = "";
        private SpriteRenderer preview;
        private static readonly Color Gold = TournamentMenu.Gold, Pale = TournamentMenu.Pale;
        private static List<string> palette;

        public static void Open(TourEntry e)
        {
            if (open != null) return;
            var go = new GameObject("Overtime Tournament Editor");
            open = go.AddComponent<TournamentEditor>();
            if (!open.InitKit(200)) { Destroy(go); open = null; return; }
            open.HoldTitle();
            open.entry = e;
            open.work = e != null ? Tournaments.Content(e) : NewTournament();
            if (open.work == null) open.work = NewTournament();
            open.Build();
        }

        public static void CloseNow() { if (open != null) { open.Pull(); open.Store(); Destroy(open.gameObject); } }

        // ------------------------------------------------------------ defaults

        private static Tournament NewTournament()
        {
            var t = new Tournament { name = $"MY TOURNAMENT {Tournaments.All.Count(e => e.mine) + 1}", mode = "fixed" };
            var picks = Teams.All.Where(x => x.Slot == "regular").OrderBy(_ => UnityEngine.Random.value).Take(2).Concat(Teams.All.Where(x => x.Slot == "boss").OrderBy(_ => UnityEngine.Random.value).Take(1));
            t.teams = picks.Select(FromTeam).ToArray();
            for (int i = 0; i < t.teams.Length; i++) for (int s = 0; s < 7; s++) t.teams[i].levels[s] = Mathf.Min(3, i);
            t.player.perks = new[] { "blaze_spike", "quick_set" }.Where(id => PerkRegistry.Get(id) != null).ToArray();
            return t;
        }

        // a team that looks like one of the mod's own (its name, colours, emblem and signature perk)
        private static TourTeam FromTeam(TeamDef d)
        {
            var tm = new TourTeam { name = d.Name.Length <= Rules.TeamNameMax ? d.Name : d.Name.Substring(0, Rules.TeamNameMax), intro = d.Intro.Length <= Tournaments.IntroMax ? d.Intro : "", @base = d.Base };
            CopyLook(tm, d);
            tm.perks = d.Signature.Where(id => PerkRegistry.Get(id) != null).Take(Rules.TeamMaxPerks).ToArray();
            return tm;
        }

        private static void CopyLook(TourTeam tm, TeamDef d)
        {
            tm.jersey = d.Jersey; tm.shorts = d.Shorts; tm.banner = d.Banner;
            tm.hair = new[] { d.Hair[0], d.Hair[Math.Min(1, d.Hair.Length - 1)] };
            if (d.Emblem != null && d.Emblem.Length >= 4) tm.emblem = new TourEmblem { mascot = d.Emblem[0], layout = d.Emblem[1], font = d.Emblem[2], accent = d.Emblem[3] };
        }

        private static List<string> Palette()
        {
            if (palette != null) return palette;
            var all = Teams.All.SelectMany(t => new[] { t.Jersey, t.Shorts, t.Banner }.Concat(t.Hair).Concat(t.Emblem != null && t.Emblem.Length > 3 ? new[] { t.Emblem[3] } : new string[0]))
                .Concat(new[] { "FFFFFF", "1A1A1A", "808080" }).Select(h => h.ToUpperInvariant()).Distinct().ToList();
            palette = all.OrderBy(h => { Color.RGBToHSV(Vfx.Hex(h), out var hh, out var ss, out var vv); return ss < 0.15f ? 2f + vv : hh; }).ToList();
            return palette;
        }

        // ------------------------------------------------------------ layout

        private void Build()
        {
            int keep = focus;
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops();
            nameField = descField = null; preview = null;
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            Box("Panel", Vector2.zero, new Vector2(W * 0.96f, H * 0.94f), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            if (pageNo >= work.teams.Length) pageNo = -2;
            if (pageNo == -2) TournamentPage();
            else if (pageNo == -1) LoadoutPage();
            else TeamPage(pageNo);
            navKeys = true;
            focus = keep >= 0 && keep < stops.Count ? keep : 0;
        }

        private float PW => W * 0.96f;
        private float Top => H * 0.47f;

        private void TournamentPage()
        {
            float pw = PW, top = Top;
            Text(entry == null ? "NEW TOURNAMENT" : "EDIT TOURNAMENT", new Vector2(0f, top - 1.6f), 1.0f, pw - 2f, Color.white);
            float lx = -pw * 0.25f, lw = pw * 0.45f, y = top - 4.2f;
            Text("NAME", new Vector2(lx - lw / 2f + 2.2f, y), 0.45f, 4f, Pale);
            nameField = TextField(new Vector2(lx + 2f, y), new Vector2(lw - 4.5f, 1.8f), "TOURNAMENT NAME", Rules.TournamentNameMax, v => { Pull(); Build(); }, work.name);
            y -= 2.3f;
            Text("ABOUT", new Vector2(lx - lw / 2f + 2.2f, y), 0.45f, 4f, Pale);
            descField = TextField(new Vector2(lx + 2f, y), new Vector2(lw - 4.5f, 1.8f), "A LINE ABOUT IT (OPTIONAL)", Rules.DescriptionMax, v => { Pull(); Build(); }, work.desc);
            y -= 2.6f;
            var tb = Button(work.Fixed ? "TYPE: FIXED LOADOUT" : "TYPE: NORMAL DRAFT", new Vector2(lx, y), new Vector2(lw * 0.6f, 1.9f), () => Change(() => work.mode = work.Fixed ? "draft" : "fixed"), 0.5f);
            y -= 1.6f;
            Text(work.Fixed ? "YOU SET THE PLAYER'S STAT LEVELS, PERKS AND CARDS. NO PICKS DURING THE RUN." : "THE PLAYER STARTS WITH NOTHING AND PICKS AFTER EVERY WIN, AS IN CLASSIC.",
                 new Vector2(lx, y), 0.38f, lw, Pale, height: 1.6f, wrap: true);
            y -= 2.2f;
            Spin("MATCHES", work.teams.Length.ToString(), null, new Vector2(lx - lw * 0.7f * 0.18f, y), lw * 0.7f, d => Change(() => SetMatches(work.teams.Length + d)));   // value box centred under TYPE
            y -= 2.6f;
            if (work.Fixed)
                Button($"YOUR TEAM:  {work.player.levels.Sum()} STAT LEVELS · {work.player.perks.Length} PERKS · {work.player.cards.Length} CARDS", new Vector2(lx, y), new Vector2(lw, 2f), () => Go(-1), 0.45f);
            else Text("YOUR TEAM: PICKS ITS UPGRADES AS IT GOES (DRAFT)", new Vector2(lx, y), 0.42f, lw, Pale);
            y -= 3.0f;
            int diff = Tournaments.Difficulty(work);
            Text($"DIFFICULTY {diff}/10", new Vector2(lx, y), 0.8f, lw, diff >= 8 ? new Color(1f, 0.5f, 0.4f) : diff >= 5 ? Gold : new Color(0.55f, 1f, 0.6f));
            Text($"ESTIMATED CLEAR CHANCE {Tournaments.Pct(Tournaments.ClearChance(work))}  ·  FROM THE MATCH SIMULATOR, A ROUGH GUIDE", new Vector2(lx, y - 1.2f), 0.36f, lw, Pale);

            // the matches
            float rx = pw * 0.25f, rw = pw * 0.44f, ry = top - 4.2f;
            Text("MATCHES (THE LAST ONE IS THE FINAL)", new Vector2(rx, ry), 0.45f, rw, Gold);
            ry -= 1.9f;
            for (int i = 0; i < work.teams.Length; i++)
            {
                var tm = work.teams[i];
                int k = i;
                Shade(Button("", new Vector2(rx, ry), new Vector2(rw, 2.25f), () => Go(k), 0.5f), ListColor);
                Box("Swatch", new Vector2(rx - rw / 2f + 0.8f, ry), new Vector2(0.7f, 1.7f), Vfx.Hex(tm.jersey), 7);
                Text($"<noparse>{i + 1}. {(tm.name.Length > 0 ? tm.name.ToUpperInvariant() : "(NO NAME)")}</noparse>", new Vector2(rx + 0.6f, ry + 0.42f), 0.5f, rw - 2.4f, Color.white, TextAlignmentOptions.Left);
                Text($"{tm.@base.ToUpperInvariant()} · LEVELS {tm.levels.Sum()} · {tm.perks.Length} PERKS · {tm.cards.Length} CARDS · YOU WIN ~{Tournaments.Pct(Tournaments.WinChance(work, i))}",
                     new Vector2(rx + 0.6f, ry - 0.5f), 0.34f, rw - 2.4f, Pale, TextAlignmentOptions.Left);
                ry -= 2.45f;
            }

            Footer(true);
        }

        private void Footer(bool main)
        {
            float pw = PW, by = -H * 0.47f + 1.6f;
            string why = Tournaments.Problem(work);
            string verify = entry != null && Tournaments.Verified(entry) && entry.content == Tournaments.Encode(work) ? "CLEARED BY YOU: READY TO PUBLISH" : "NOT CLEARED YET: BEAT IT ONCE (TEST PLAY) TO PUBLISH IT";
            string line = status.Length > 0 ? status : why ?? verify;
            Text($"<noparse>{line}</noparse>", new Vector2(0f, by + 2f), 0.42f, pw - 2f, why != null && status.Length == 0 ? new Color(1f, 0.6f, 0.5f) : Gold);
            if (main)
            {
                Button("SAVE", new Vector2(-pw * 0.33f, by), new Vector2(pw * 0.14f, 2f), () => { Pull(); Store(); status = "SAVED"; Build(); }, 0.55f);
                Button("TEST PLAY", new Vector2(-pw * 0.16f, by), new Vector2(pw * 0.14f, 2f), TestPlay, 0.55f);
                Button("COPY CODE", new Vector2(0f, by), new Vector2(pw * 0.14f, 2f), CopyCode, 0.55f);
                Button("CLOSE", new Vector2(pw * 0.33f, by), new Vector2(pw * 0.14f, 2f), Close, 0.55f);
                PadHint("A: CHOOSE · B: CLOSE (SAVES)", new Vector2(pw * 0.17f, by), pw * 0.18f);
            }
            else
            {
                Button("DONE", new Vector2(pw * 0.33f, by), new Vector2(pw * 0.14f, 2f), () => Go(-2), 0.6f);
                PadHint("A: CHOOSE · B: DONE · LB / RB: OTHER MATCHES", new Vector2(pw * 0.1f, by), pw * 0.3f);
            }
        }

        private void TeamPage(int k)
        {
            var tm = work.teams[k];
            float pw = PW, top = Top;
            Text($"MATCH {k + 1} OF {work.teams.Length}{(k == work.teams.Length - 1 ? " (FINAL)" : "")}", new Vector2(0f, top - 1.5f), 0.9f, pw - 2f, Color.white);

            // left: identity, look, emblem
            float lx = -pw * 0.34f, lw = pw * 0.28f, y = top - 3.8f, step = 1.95f;
            nameField = TextField(new Vector2(lx, y), new Vector2(lw, 1.7f), "TEAM NAME", Rules.TeamNameMax, v => { Pull(); Build(); }, tm.name);
            y -= step;
            descField = TextField(new Vector2(lx, y), new Vector2(lw, 1.7f), "WHAT THEY SAY AT KICK-OFF", Tournaments.IntroMax, v => { Pull(); Build(); }, tm.intro);
            y -= step;
            int b = Array.IndexOf(Tournaments.Bases, tm.@base);
            Spin("SQUAD", tm.@base.ToUpperInvariant(), null, new Vector2(lx, y), lw, d => Change(() => tm.@base = Tournaments.Bases[(Math.Max(0, b) + d + Tournaments.Bases.Length) % Tournaments.Bases.Length]));
            y -= step;
            Spin("COPY LOOK", "FROM A TEAM", null, new Vector2(lx, y), lw, d => Change(() => { lookFrom = (lookFrom + d + Teams.All.Length) % Teams.All.Length; CopyLook(tm, Teams.All[lookFrom]); status = "LOOK OF " + Teams.All[lookFrom].Name.ToUpperInvariant(); }));
            y -= step;
            ColorSpin("JERSEY", tm.jersey, new Vector2(lx, y), lw, v => tm.jersey = v); y -= step;
            ColorSpin("SHORTS", tm.shorts, new Vector2(lx, y), lw, v => tm.shorts = v); y -= step;
            ColorSpin("HAIR 1", tm.hair[0], new Vector2(lx, y), lw, v => tm.hair[0] = v); y -= step;
            ColorSpin("HAIR 2", tm.hair[1], new Vector2(lx, y), lw, v => tm.hair[1] = v); y -= step;
            ColorSpin("BANNER", tm.banner, new Vector2(lx, y), lw, v => tm.banner = v); y -= step;
            var e = tm.emblem;
            int mi = Array.IndexOf(Rules.Mascots, e.mascot);
            Spin("MASCOT", e.mascot.Replace('_', ' ').ToUpperInvariant(), null, new Vector2(lx, y), lw, d => Change(() => e.mascot = Rules.Mascots[(Math.Max(0, mi) + d + Rules.Mascots.Length) % Rules.Mascots.Length])); y -= step;
            int li = Array.IndexOf(Tournaments.Layouts, e.layout);
            Spin("LAYOUT", e.layout.ToUpperInvariant(), null, new Vector2(lx, y), lw, d => Change(() => e.layout = Tournaments.Layouts[(Math.Max(0, li) + d + 3) % 3])); y -= step;
            int fi = Array.IndexOf(Tournaments.Fonts, e.font);
            Spin("LETTERING", e.font.ToUpperInvariant(), null, new Vector2(lx, y), lw, d => Change(() => e.font = Tournaments.Fonts[(Math.Max(0, fi) + d + 2) % 2])); y -= step;
            ColorSpin("ACCENT", e.accent, new Vector2(lx, y), lw, v => e.accent = v);

            // middle: the emblem on its banner, the team in short
            float mx = -pw * 0.02f, my = top - 8.6f;
            var cloth = Color.Lerp(Vfx.Hex(tm.banner), Color.black, 0.42f);
            Box("Cloth", new Vector2(mx, my), new Vector2(13f, 10.5f), cloth, 3);
            var tex = EmblemArt.Make(tm.emblem, tm.name.Length > 0 ? tm.name : "TEAM");
            if (tex != null)
            {
                preview = new GameObject("Emblem").AddComponent<SpriteRenderer>();
                preview.transform.SetParent(transform, false);
                preview.transform.localPosition = new Vector3(mx, my, -0.05f);
                preview.sprite = EmblemArt.SpriteOf(tex, tex.width / 11.5f);
                preview.sortingLayerID = layer; preview.sortingOrder = order + 6;
            }
            else Text("EMBLEM PIECES MISSING", new Vector2(mx, my), 0.5f, 12f, Gold);
            my -= 6.6f;
            Text($"<noparse>{(tm.name.Length > 0 ? tm.name.ToUpperInvariant() : "(NO NAME)")}</noparse>", new Vector2(mx, my), 0.6f, 15f, Color.white);
            Text($"YOU WIN ~{Tournaments.Pct(Tournaments.WinChance(work, k))} (SIMULATOR)", new Vector2(mx, my - 1.1f), 0.42f, 15f, Gold);
            if (tm.name.Any(c => char.IsLetterOrDigit(c) && !"ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".Contains(char.ToUpperInvariant(c))))
                Text("THE EMBLEM LEAVES OUT LETTERS IT CAN'T DRAW", new Vector2(mx, my - 2f), 0.34f, 15f, Pale);
            // order
            Button("< EARLIER", new Vector2(mx - 3.6f, my - 3.4f), new Vector2(6.6f, 1.7f), () => Move(k, -1), 0.45f);
            Button("LATER >", new Vector2(mx + 3.6f, my - 3.4f), new Vector2(6.6f, 1.7f), () => Move(k, 1), 0.45f);
            if (work.teams.Length > Rules.TournamentMinMatches)
                Button("REMOVE THIS MATCH", new Vector2(mx, my - 5.4f), new Vector2(13.8f, 1.7f), () => Change(() => { work.teams = work.teams.Where((_, i) => i != k).ToArray(); pageNo = -2; }), 0.45f);

            // right: stats, perks, cards
            float rx = pw * 0.3f, rw = pw * 0.36f, ry = top - 3.8f;
            Text("STAT LEVELS (0-3)", new Vector2(rx, ry), 0.45f, rw, Gold);
            ry -= 1.6f;
            for (int s = 0; s < 7; s++)
            {
                int si = s;
                Spin(StatCards.Label(Tournaments.Stats[s]).ToUpperInvariant(), tm.levels[s].ToString(), null, new Vector2(rx, ry), rw * 0.8f, d => Change(() => tm.levels[si] = Mathf.Clamp(tm.levels[si] + d, 0, Rules.StatMaxLevel)), 1.5f);
                ry -= 1.55f;
            }
            ry -= 0.3f;
            PerkList("PERKS", tm.perks, Rules.TeamMaxPerks, new Vector2(rx - rw / 4f - 0.15f, ry), rw / 2f - 0.3f, false, v => tm.perks = v);
            PerkList("STAT CARDS", tm.cards, Rules.TeamMaxCards, new Vector2(rx + rw / 4f + 0.15f, ry), rw / 2f - 0.3f, true, v => tm.cards = v);

            Footer(false);
        }

        private static int lookFrom;

        private void LoadoutPage()
        {
            var p = work.player;
            float pw = PW, top = Top;
            Text("YOUR TEAM (FIXED LOADOUT)", new Vector2(0f, top - 1.5f), 0.9f, pw - 2f, Color.white);
            Text("WHAT THE PLAYER STARTS WITH AND KEEPS FOR THE WHOLE TOURNAMENT: NO NEW PERKS OR CARDS ON THE WAY.", new Vector2(0f, top - 3f), 0.4f, pw - 2f, Pale);
            float lx = -pw * 0.3f, lw = pw * 0.3f, y = top - 5f;
            Text("STAT LEVELS (0-3)", new Vector2(lx, y), 0.45f, lw, Gold);
            y -= 1.7f;
            for (int s = 0; s < 7; s++)
            {
                int si = s;
                Spin(StatCards.Label(Tournaments.Stats[s]).ToUpperInvariant(), p.levels[s].ToString(), null, new Vector2(lx, y), lw, d => Change(() => p.levels[si] = Mathf.Clamp(p.levels[si] + d, 0, Rules.StatMaxLevel)));
                y -= 1.9f;
            }
            PerkList("PERKS", p.perks, Rules.LoadoutMaxPerks, new Vector2(pw * 0.08f, top - 5f), pw * 0.24f, false, v => p.perks = v);
            PerkList("STAT CARDS", p.cards, Rules.LoadoutMaxCards, new Vector2(pw * 0.34f, top - 5f), pw * 0.24f, true, v => p.cards = v);
            Footer(false);
        }

        // ------------------------------------------------------------ controls

        // "LABEL   <  VALUE  >": one stop (left / right or A change it); the arrows are for the mouse
        private void Spin(string label, string value, Color? swatch, Vector2 at, float w, Action<int> step, float h = 1.7f)
        {
            float lw = w * 0.36f;
            Text(label, new Vector2(at.x - w / 2f + lw / 2f, at.y), 0.4f, lw, Pale, TextAlignmentOptions.Left);
            float ax = at.x - w / 2f + lw + 0.9f, bx = at.x + w / 2f - 0.9f;
            float mid = (ax + bx) / 2f, span = bx - ax - 2f;
            var row = Box("Value", new Vector2(mid, at.y), new Vector2(bx - ax + 1.6f, h - 0.1f), new Color(0.02f, 0.2f, 0.34f, 1f), 4);
            AddStop(row, () => step(1)).Adjust = step;
            foreach (var (x, d) in new[] { (ax, -1), (bx, 1) })
            {
                var b = Button(d < 0 ? "<" : ">", new Vector2(x, at.y), new Vector2(1.6f, h - 0.1f), () => step(d), 0.55f);
                stops.RemoveAll(s => s.Box == b);
            }
            if (swatch.HasValue) Box("Swatch", new Vector2(mid, at.y), new Vector2(span, h - 0.4f), swatch.Value, 6);
            Text(value, new Vector2(mid, at.y), 0.45f, span, swatch.HasValue ? (swatch.Value.grayscale > 0.6f ? Color.black : Color.white) : Color.white);
        }

        private void ColorSpin(string label, string hex, Vector2 at, float w, Action<string> set)
        {
            var pal = Palette();
            int i = pal.IndexOf((hex ?? "").ToUpperInvariant());
            if (i < 0) i = Nearest(pal, hex);
            Spin(label, "#" + hex, Vfx.Hex(hex), at, w, d => Change(() => set(pal[(i + d + pal.Count) % pal.Count])));
        }

        private static int Nearest(List<string> pal, string hex)
        {
            var c = Vfx.Hex(hex);
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < pal.Count; i++) { var p = Vfx.Hex(pal[i]); float d = (p.r - c.r) * (p.r - c.r) + (p.g - c.g) * (p.g - c.g) + (p.b - c.b) * (p.b - c.b); if (d < bd) { bd = d; best = i; } }
            return best;
        }

        // the chosen perks / cards (click one to take it off) and + ADD
        private void PerkList(string title, string[] ids, int max, Vector2 at, float w, bool cards, Action<string[]> set)
        {
            Text($"{title} ({ids.Length}/{max})", at, 0.42f, w, Gold);
            float y = at.y - 1.5f;
            for (int i = 0; i < ids.Length; i++)
            {
                int k = i;
                string label = cards ? StatCards.Get(ids[i])?.Title ?? ids[i] : PerkRegistry.Get(ids[i])?.Def.Title ?? ids[i];
                string rarity = cards ? StatCards.Get(ids[i])?.Rarity : PerkRegistry.Get(ids[i])?.Def.Rarity;
                Shade(Button("", new Vector2(at.x, y), new Vector2(w, 1.35f), () => Change(() => set(ids.Where((_, j) => j != k).ToArray())), 0.4f), ListColor);
                Text($"<color={TournamentMenu.Hex(StatCards.RarityColor(rarity ?? "common"))}>{label.ToUpperInvariant()}</color>  <size=70%>×</size>", new Vector2(at.x, y), 0.38f, w - 0.4f, Color.white);
                y -= 1.5f;
            }
            if (ids.Length < max)
                Button(cards ? "+ ADD CARD" : "+ ADD PERK", new Vector2(at.x, y), new Vector2(w, 1.4f), () => { Pull(); TourPicker.Open(cards, ids, id => Change(() => set(cards ? ids.Concat(new[] { id }).ToArray() : ids.Contains(id) ? ids : ids.Concat(new[] { id }).ToArray()))); }, 0.42f);
        }

        // ------------------------------------------------------------ edits

        private void Pull()
        {
            if (pageNo == -2)
            {
                if (nameField != null) work.name = nameField.Value.Trim();
                if (descField != null) work.desc = descField.Value.Trim();
            }
            else if (pageNo >= 0 && pageNo < work.teams.Length)
            {
                if (nameField != null) work.teams[pageNo].name = nameField.Value.Trim();
                if (descField != null) work.teams[pageNo].intro = descField.Value.Trim();
            }
        }

        private void Change(Action a) { Pull(); a(); status = ""; if (this != null) Build(); }

        private void Go(int p) { Pull(); pageNo = p; status = ""; focus = -1; Build(); }

        private void SetMatches(int n)
        {
            n = Mathf.Clamp(n, Rules.TournamentMinMatches, Rules.TournamentMaxMatches);
            if (n == work.teams.Length) return;
            var list = work.teams.ToList();
            while (list.Count > n) list.RemoveAt(list.Count - 1);
            while (list.Count < n)
            {
                var d = Teams.All[UnityEngine.Random.Range(0, Teams.All.Length)];
                var tm = FromTeam(d);
                for (int s = 0; s < 7; s++) tm.levels[s] = Mathf.Min(3, list.Count / 2);
                list.Add(tm);
            }
            work.teams = list.ToArray();
        }

        private void Move(int k, int d)
        {
            int j = k + d;
            if (j < 0 || j >= work.teams.Length) return;
            Change(() => { (work.teams[k], work.teams[j]) = (work.teams[j], work.teams[k]); pageNo = j; });
        }

        // the working copy into this PC's list (a new version if anything changed)
        private void Store()
        {
            if (entry == null)
            {
                if (work.name.Length == 0 && work.teams.All(t => t.name.Length == 0)) return;
                entry = Tournaments.Add(new TourEntry { content = Tournaments.Encode(work), mine = true, added = Tournaments.Now });
            }
            else if (!Tournaments.Update(entry, work)) return;
            Plugin.Log.LogInfo($"Tournament saved: {work.name} ({entry.Hash}, {work.mode}, {work.teams.Length} matches, difficulty {Tournaments.Difficulty(work)})");
        }

        private void TestPlay()
        {
            Pull();
            var why = Tournaments.Problem(work);
            if (why != null) { status = why; Build(); return; }
            Store();
            TournamentMenu.Play(entry, true);
        }

        private void CopyCode()
        {
            Pull();
            var why = Tournaments.Problem(work);
            if (why != null) { status = why; Build(); return; }
            Store();
            var code = Tournaments.Code(entry);
            GUIUtility.systemCopyBuffer = code;
            status = $"CODE COPIED ({code.Length} CHARACTERS)" + (Tournaments.Verified(entry) ? "" : ": NOT CLEARED BY YOU YET");
            Build();
        }

        private void Close()
        {
            Pull();
            Store();
            Destroy(gameObject);
            TournamentMenu.Open(entry != null ? "SAVED" : null);
        }

        private void Update()
        {
            if (TourPicker.IsOpen) return;
            if (FieldKeys() || Typing) return;
            if (NavUpdate()) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) OnNavBack();
        }

        protected override void OnNavBack() { if (pageNo == -2) Close(); else Go(-2); }
        protected override void OnNavTab(int dir) { if (pageNo >= 0) Go((pageNo + dir + work.teams.Length) % work.teams.Length); }
        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }
    }

    // ADD PERK / ADD CARD: every perk (or stat card) of the mod on pages, filtered by element (rarity for cards); the focused
    // or pointed-at one is described at the bottom with its simulator value.
    internal class TourPicker : OvertimePanel
    {
        private static TourPicker open;
        public static bool IsOpen => open != null;
        private bool cards;
        private HashSet<string> have;
        private Action<string> pick;
        private string filter = "all";
        private int page;
        private const int Cols = 3, RowsN = 8;
        private readonly List<(SpriteRenderer box, string id)> cells = new List<(SpriteRenderer, string)>();
        private TextMeshPro about;
        private string shown;

        public static void Open(bool cards, IEnumerable<string> have, Action<string> onPick)
        {
            if (open != null) return;
            var go = new GameObject("Overtime Picker");
            open = go.AddComponent<TourPicker>();
            if (!open.InitKit(260)) { Destroy(go); open = null; return; }
            open.cards = cards; open.have = new HashSet<string>(have ?? new string[0]); open.pick = onPick;
            open.Build();
        }

        private string[] Filters => cards ? new[] { "all", "common", "rare", "epic" } : new[] { "all" }.Concat(Elements.All.Select(e => e.Id).Where(id => Perks.All.Any(p => p.Element == id))).ToArray();

        private List<string> Items()
        {
            if (cards) return StatCards.All.Where(c => filter == "all" || c.Rarity == filter).Select(c => c.Id).ToList();
            return Perks.All.Where(p => (filter == "all" || p.Element == filter) && !have.Contains(p.Id) && PerkRegistry.Get(p.Id) != null).Select(p => p.Id).ToList();
        }

        private void Build()
        {
            int keep = focus;
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops(); cells.Clear(); shown = null;
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.05f, 0.12f, 0.75f), 0);
            float pw = W * 0.9f, ph = H * 0.9f, top = ph / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.04f, 0.33f, 0.55f, 0.99f), 1);
            Text(cards ? "ADD A STAT CARD" : "ADD A PERK", new Vector2(0f, top - 1.5f), 0.9f, pw - 2f, Color.white);
            var fs = Filters;
            float fw = Mathf.Min(5.2f, (pw - 2f) / fs.Length - 0.2f), fx = -(fs.Length - 1) * (fw + 0.2f) / 2f;
            for (int i = 0; i < fs.Length; i++)
            {
                string f = fs[i];
                var b = Button(f.ToUpperInvariant(), new Vector2(fx + i * (fw + 0.2f), top - 3.3f), new Vector2(fw, 1.5f), () => { filter = f; page = 0; focus = -1; Build(); }, 0.36f);
                if (f == filter) Shade(b, new Color(0.96f, 0.55f, 0.12f, 1f));
            }
            var items = Items();
            int per = Cols * RowsN, pages = Math.Max(1, (items.Count + per - 1) / per);
            page = Mathf.Clamp(page, 0, pages - 1);
            float cw = (pw - 2f) / Cols, y0 = top - 5.4f;
            for (int i = page * per; i < Math.Min(items.Count, (page + 1) * per); i++)
            {
                int r = (i - page * per) / Cols, c = (i - page * per) % Cols;
                string id = items[i];
                var at = new Vector2(-pw / 2f + 1f + cw * (c + 0.5f), y0 - r * 1.75f);
                string title, rarity, el = "none";
                if (cards) { var d = StatCards.Get(id); title = d.Title; rarity = d.Rarity; }
                else { var d = PerkRegistry.Get(id).Def; title = d.Title; rarity = d.Rarity; el = d.Element; }
                var box = Shade(Button("", at, new Vector2(cw - 0.4f, 1.55f), () => Pick(id), 0.4f), ListColor);
                string elTag = el != "none" ? $"  <size=70%><color={TournamentMenu.Hex(ElementColor(el))}>{el.ToUpperInvariant()}</color></size>" : "";
                Text($"<color={TournamentMenu.Hex(StatCards.RarityColor(rarity))}>{title.ToUpperInvariant()}</color>{elTag}", at, 0.42f, cw - 0.8f, Color.white);
                cells.Add((box, id));
            }
            float by = -ph / 2f + 1.6f;
            about = Text("", new Vector2(0f, by + 3.1f), 0.42f, pw - 3f, TournamentMenu.Pale, height: 2.6f, wrap: true);
            if (pages > 1)
            {
                Button("<", new Vector2(-pw * 0.3f, by), new Vector2(2f, 1.7f), () => { page = (page - 1 + pages) % pages; Build(); }, 0.6f);
                Text($"PAGE {page + 1}/{pages}", new Vector2(-pw * 0.22f, by), 0.45f, 6f, TournamentMenu.Pale);
                Button(">", new Vector2(-pw * 0.14f, by), new Vector2(2f, 1.7f), () => { page = (page + 1) % pages; Build(); }, 0.6f);
            }
            Button("BACK", new Vector2(pw * 0.3f, by), new Vector2(pw * 0.16f, 2f), () => Destroy(gameObject), 0.6f);
            PadHint("A: ADD · B: BACK · LB / RB: PAGES", new Vector2(pw * 0.06f, by), pw * 0.3f);
            navKeys = true;
            focus = keep >= 0 && keep < stops.Count ? keep : fs.Length;
        }

        private static Color ElementColor(string id) { var e = Elements.All.FirstOrDefault(x => x.Id == id); return e.Id != null ? Vfx.Hex(e.Color) : Color.white; }

        private void Describe(string id)
        {
            if (id == shown || about == null) return;
            shown = id;
            if (id == null) { about.text = ""; return; }
            int i;
            if (cards)
            {
                var c = StatCards.Get(id);
                i = Array.IndexOf(Draft.CardIds, id);
                about.text = $"<noparse>{c.Title.ToUpperInvariant()}  ·  {c.Rarity.ToUpperInvariant()}  ·  {string.Join("  ", c.Stats.Select((s, k) => $"{StatCards.Label(s).ToUpperInvariant()} {(c.Points[k] > 0 ? "+" : "")}{c.Points[k]:0.##}"))}</noparse>\n<noparse>{c.Blurb}</noparse>" +
                             (i >= 0 ? $"  <color=#FFD95A>(SIMULATOR VALUE {Draft.CardPower[i] * 100f:+0.0;-0.0})</color>" : "");
            }
            else
            {
                var d = PerkRegistry.Get(id).Def;
                i = Array.IndexOf(Draft.PerkIds, id);
                about.text = $"<noparse>{d.Title.ToUpperInvariant()}  ·  {d.Rarity.ToUpperInvariant()}  ·  {d.Element.ToUpperInvariant()}</noparse>\n<noparse>{d.Description}</noparse>" +
                             (i >= 0 ? $"  <color=#FFD95A>(SIMULATOR VALUE {Draft.PerkPower[i] * 100f:+0.0;-0.0})</color>" : "");
            }
        }

        private void Pick(string id) { var cb = pick; Destroy(gameObject); cb?.Invoke(id); }

        private void Update()
        {
            // describe what the mouse points at, else what has the focus
            string over = null;
            var cam = Camera.main; var mouse = UnityEngine.InputSystem.Mouse.current;
            if (cam != null && mouse != null)
            {
                var wp = cam.ScreenToWorldPoint(mouse.position.ReadValue());
                foreach (var (box, id) in cells) if (box != null && box.bounds.Contains(new Vector3(wp.x, wp.y, box.bounds.center.z))) { over = id; break; }
            }
            if (over == null && focus >= 0 && focus < stops.Count) over = cells.FirstOrDefault(c => c.box == stops[focus].Box).id;
            Describe(over);
            if (NavUpdate()) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Destroy(gameObject);
        }

        protected override void OnNavBack() => Destroy(gameObject);
        protected override void OnNavTab(int dir)
        {
            int pages = Math.Max(1, (Items().Count + Cols * RowsN - 1) / (Cols * RowsN));
            page = (page + dir + pages) % pages;
            Build();
        }
        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }
    }
}

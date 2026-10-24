using System;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HangtimeOvertime.Online;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // TOURNAMENTS (title bar), laid out as the overhaul's mockup: a gold ribbon, folder tabs MY TOURNAMENTS / BROWSE ONLINE
    // (LB / RB), big cards on the left (name, badges, the teams' emblems, a flame meter for the difficulty, your record) and
    // the selected tournament as a poster on the right (its matches as a ladder with emblems and your win chance, one big
    // PLAY, icon buttons). MY TOURNAMENTS: made here, added from a code or from the browser; EDIT / CODE / PUBLISH / DELETE;
    // NEW TOURNAMENT and ADD CODE (PASTE). BROWSE ONLINE: everyone's published tournaments, sorted and filtered, with
    // players, clear rate, rating and fastest clears; PLAY, ADD, RATE 1-5, REPORT. Pad: A choose, B back, X delete, Y code.
    internal class TournamentMenu : OvertimePanel
    {
        private static TournamentMenu open;
        public static bool IsOpen => open != null;
        private static int page, selected;
        private static string status = "";
        private static bool online;
        private bool confirmDelete, confirmReport;
        private const int Cards = 3;

        public static void Open(string message = null)
        {
            if (open != null) return;
            var go = new GameObject("Overtime Tournaments");
            open = go.AddComponent<TournamentMenu>();
            if (!open.InitKit(200)) { Destroy(go); open = null; return; }
            open.HoldTitle();
            status = message ?? "";
            if (online) open.Refresh(); else open.Build();
        }

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
        public static readonly Color Gold = new Color(1f, 0.85f, 0.35f), Pale = new Color(0.78f, 0.88f, 1f), Bad = new Color(1f, 0.55f, 0.5f);
        private static readonly Color Green = new Color(0.49f, 0.9f, 0.47f), Coral = new Color(1f, 0.42f, 0.36f), Sky = new Color(0.25f, 0.77f, 0.94f);
        private static readonly Color CardBlue = new Color(0.094f, 0.275f, 0.46f, 1f), PosterBack = new Color(0.03f, 0.11f, 0.2f, 1f), Badge = new Color(0.03f, 0.094f, 0.17f, 0.92f);

        // the mockup's coordinates (1600 x 900 px, 25 px a layout unit, x scaled on screens that aren't 16:9)
        private float KX => W / 64f;
        private Vector2 P(float px, float py) => new Vector2((px - 800f) / 25f * KX, (450f - py) / 25f);
        private Vector2 S(float pw, float ph) => new Vector2(pw / 25f * KX, ph / 25f);
        private static float F(float px) => px / 40f;                          // a mockup font size -> the kit's text size
        private TextMeshPro TL(string s, float px, float py, float size, Color c, float width) => Text(s, P(px + width / 2f, py), F(size), width / 25f * KX, c, TextAlignmentOptions.Left);
        private TextMeshPro TR(string s, float px, float py, float size, Color c, float width) => Text(s, P(px - width / 2f, py), F(size), width / 25f * KX, c, TextAlignmentOptions.Right);
        private TextMeshPro TC(string s, float px, float py, float size, Color c, float width) => Text(s, P(px, py), F(size), width / 25f * KX, c);
        private SpriteRenderer Rect(string name, float x0, float y0, float x1, float y1, Color c, int o, bool outline = false) => Box(name, P((x0 + x1) / 2f, (y0 + y1) / 2f), S(x1 - x0, y1 - y0), c, o, outline);
        private SpriteRenderer Btn(string label, float x0, float y0, float x1, float y1, Action act, float size = 26f) => Button(label, P((x0 + x1) / 2f, (y0 + y1) / 2f), S(x1 - x0, y1 - y0), act, F(size));
        private static void Tilt(Component c, float deg) { if (c != null) c.transform.localRotation = Quaternion.Euler(0f, 0f, deg); }

        private void Build()
        {
            int keep = focus;
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops();
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0.024f, 0.086f, 0.17f, 0.72f), 0);

            // folder tabs, the panel over their bottoms, the ribbon
            var my = Shade(Btn("", 60, 92, 330, 150, () => SetTab(false)), online ? UiArt.Blue : UiArt.Navy);
            Icon("trophy", P(94, 118), 1.2f, online ? UiArt.Ink : UiArt.Gold, 7);
            TC("MY TOURNAMENTS", 210, 119, 26, online ? UiArt.Ink : UiArt.Gold, 200);
            var br = Shade(Btn("", 342, 92, 612, 150, () => SetTab(true)), online ? UiArt.Navy : UiArt.Blue);
            Icon("globe", P(376, 118), 1.15f, online ? UiArt.Gold : UiArt.Ink, 7);
            TC("BROWSE ONLINE", 492, 119, 26, online ? UiArt.Gold : UiArt.Ink, 200);
            TL("LB / RB", 640, 120, 18, new Color(0.7f, 1f, 0.75f), 90);
            Box("Panel", P(800, 485), S(1520, 690), UiArt.Navy, 1);
            var ribbon = Rect("Ribbon", 560, 18, 1040, 92, UiArt.Gold, 2, true);
            Tilt(ribbon, -1.5f);
            Tilt(TC("TOURNAMENTS", 800, 55, 54, UiArt.Dark, 460), -1.5f);

            if (online) BuildOnline(); else BuildLocal();

            // footer
            if (!online)
            {
                var nb = Btn("", 70, 846, 420, 892, () => { Destroy(gameObject); TournamentEditor.Open(null); });
                Icon("plus", P(104, 869), 0.9f, UiArt.Ink, 7);
                TC("NEW TOURNAMENT", 255, 870, 26, UiArt.Ink, 300);
                Btn("", 440, 846, 760, 892, AddCode);
                Icon("share", P(474, 869), 0.9f, UiArt.Ink, 7);
                TC("ADD CODE (PASTE)", 610, 870, 26, UiArt.Ink, 260);
            }
            if (status.Length > 0) TC($"<noparse>{status}</noparse>", online ? 520 : 1000, 870, 20, Gold, online ? 820 : 420);
            Btn("", 1420, 846, 1560, 892, () => Destroy(gameObject));
            Icon("back", P(1450, 869), 0.85f, UiArt.Ink, 7);
            TC("BACK", 1500, 870, 26, UiArt.Ink, 90);
            PadHint(online ? "A: CHOOSE   B: BACK" : "A: CHOOSE   B: BACK   X: DELETE   Y: COPY CODE", P(online ? 1180 : 1300, 870), 380 / 25f * KX);
            navKeys = true;
            focus = keep >= 0 && keep < stops.Count ? keep : 0;
        }

        private void SetTab(bool on)
        {
            if (online == on) return;
            online = on; status = ""; focus = -1; confirmDelete = confirmReport = false;
            if (online) Refresh(); else Build();
        }

        // ------------------------------------------------------------ pieces

        private void Badges(float x, float y, params (string text, Color color)[] items)
        {
            foreach (var (text, color) in items)
            {
                if (string.IsNullOrEmpty(text)) continue;
                float w = text.Length * 9.6f + 28f;
                Rect("Badge", x, y - 15, x + w, y + 15, Badge, 6);
                TC($"<noparse>{text}</noparse>", x + w / 2f, y, 19, color, w);
                x += w + 10f;
            }
        }

        private void Flames(float x, float y, int lit, bool onOrange)
        {
            var on = onOrange ? new Color(0.55f, 0.1f, 0.05f, 1f) : UiArt.Orange;
            var off = onOrange ? new Color(0f, 0f, 0f, 0.16f) : new Color(1f, 1f, 1f, 0.22f);
            for (int i = 0; i < 10; i++) Icon("flame", P(x + i * 21f + 9f, y), 0.84f, i < lit ? on : off, 8);
        }

        private void EmblemTile(TourTeam tm, float cx, float cy, float w, float h, int o = 6)
        {
            Rect("Tile", cx - w / 2f, cy - h / 2f, cx + w / 2f, cy + h / 2f, UiArt.Dark, o);
            Picture(EmblemArt.Make(tm.emblem, tm.name), P(cx, cy), (h - 10f) / 25f, o + 2);
        }

        private static Color WinColor(float p) => p >= 0.6f ? Green : p >= 0.45f ? Gold : Coral;

        // a tournament's matches as a ladder between y0 and y1 (rows shrink for long tournaments)
        private void Ladder(Tournament t, float y0, float y1)
        {
            int n = t.teams.Length;
            float gap = n > 4 ? 6f : 14f, rowH = Mathf.Min(92f, (y1 - y0 - gap * (n - 1)) / n);
            bool small = rowH < 60f;
            for (int j = 0; j < n; j++)
            {
                var tm = t.teams[j];
                float ly = y0 + j * (rowH + gap), cy = ly + rowH / 2f;
                Rect("Rung", 820, ly, 1500, ly + rowH, CardBlue, 4);
                TC(j == n - 1 ? "FINAL" : $"MATCH {j + 1}", 870, cy, small ? 16 : 20, j == n - 1 ? UiArt.Gold : Pale, 90);
                EmblemTile(tm, 970, cy, small ? rowH * 1.3f : 104f, rowH - (small ? 4f : 20f), 5);
                if (small) TL($"<noparse>{tm.name.ToUpperInvariant()}</noparse>  <size=70%><color=#C8E1FF>{tm.@base.ToUpperInvariant()} · {tm.perks.Length} PERKS</color></size>", 1044, cy, rowH > 40 ? 24 : 20, UiArt.Ink, 330);
                else
                {
                    TL($"<noparse>{tm.name.ToUpperInvariant()}</noparse>", 1044, cy - 14, 30, UiArt.Ink, 330);
                    TL($"{tm.@base.ToUpperInvariant()} · {tm.perks.Length} {(tm.perks.Length == 1 ? "PERK" : "PERKS")}{(tm.cards.Length > 0 ? $" · {tm.cards.Length} {(tm.cards.Length == 1 ? "CARD" : "CARDS")}" : "")}", 1044, cy + 18, 19, Pale, 330);
                }
                float w = Tournaments.WinChance(t, j);
                if (!small) TC("YOU WIN", 1440, cy - 16, 16, Pale, 100);
                TC("~" + Tournaments.Pct(w), 1440, small ? cy : cy + 12, small ? 22 : 30, WinColor(w), 110);
            }
        }

        // ------------------------------------------------------------ my tournaments

        private void BuildLocal()
        {
            var list = Tournaments.All;
            int pages = Math.Max(1, (list.Count + Cards - 1) / Cards);
            page = Mathf.Clamp(page, 0, pages - 1);
            selected = Mathf.Clamp(selected, 0, Math.Max(0, list.Count - 1));
            if (list.Count == 0)
            {
                TC("NO TOURNAMENTS YET", 415, 380, 40, UiArt.Ink, 640);
                Text("NEW TOURNAMENT MAKES ONE, ADD CODE PASTES ONE A FRIEND SENT YOU, BROWSE ONLINE HAS EVERYONE'S.", P(415, 450), F(22), 620 / 25f * KX, Pale, height: 3f, wrap: true);
            }
            float y = 170;
            for (int i = page * Cards; i < Math.Min(list.Count, (page + 1) * Cards); i++)
            {
                LocalCard(list[i], i, y);
                y += 206;
            }
            Pager(pages, 805);
            if (list.Count > 0) Poster(list[selected]);
        }

        private void Pager(int pages, float y)
        {
            if (pages <= 1) return;
            Btn("<", 330, y - 18, 370, y + 18, () => Step(-1), 26);
            TC($"{page + 1} / {pages}", 415, y, 22, Pale, 80);
            Btn(">", 460, y - 18, 500, y + 18, () => Step(1), 26);
        }

        private void Step(int d)
        {
            if (online)
            {
                int pages = Math.Max(1, (rows.Count + Cards - 1) / Cards);
                if (d > 0 && page == pages - 1 && more) { page++; LoadPage(rows.Count); return; }
                page = (page + d + pages) % pages;
            }
            else
            {
                int pages = Math.Max(1, (Tournaments.All.Count + Cards - 1) / Cards);
                page = (page + d + pages) % pages;
            }
            Build();
        }

        private void LocalCard(TourEntry e, int i, float y)
        {
            var t = Tournaments.Content(e);
            bool sel = i == selected;
            var txt = sel ? UiArt.Dark : UiArt.Ink;
            var card = Shade(Btn("", 70, y, 760, y + 186, () => Select(i)), sel ? new Color(1f, 0.63f, 0.24f, 1f) : CardBlue);
            if (t == null) { TL("(DAMAGED)", 96, y + 34, 38, txt, 400); return; }
            TL($"<noparse>{t.name.ToUpperInvariant()}</noparse>", 96, y + 34, 38, txt, 440);
            Badges(98, y + 77, (t.Fixed ? "FIXED" : "DRAFT", t.Fixed ? Sky : Coral), ($"{t.teams.Length} {(t.teams.Length == 1 ? "MATCH" : "MATCHES")}", UiArt.Ink),
                   (e.mine ? "BY YOU" : e.creator.Length > 0 ? "BY " + e.creator.ToUpperInvariant() : "FROM A CODE", UiArt.Gold), (e.remote.Length > 0 ? "ONLINE" : null, Green));
            for (int k = 0; k < Math.Min(5, t.teams.Length); k++) EmblemTile(t.teams[k], 130 + k * 92, y + 135, 80, 62);
            if (t.teams.Length > 5) TC($"+{t.teams.Length - 5}", 130 + 5 * 92 - 20, y + 135, 26, txt, 60);
            int diff = Tournaments.Difficulty(t);
            TR("DIFFICULTY", 660, y + 36, 18, txt, 120);
            TC($"{diff}/10", 705, y + 34, 30, txt, 80);
            Flames(546, y + 72, diff, sel);
            string rec = e.attempts > 0 ? $"CLEARED {e.clears}/{e.attempts}" + (e.best > 0f ? " · BEST " + RunClock.Format(e.best) : "") : "NOT PLAYED YET";
            TR(rec, 740, y + 160, 19, txt, 260);
        }

        private void Poster(TourEntry e)
        {
            var t = Tournaments.Content(e);
            Rect("Poster", 790, 170, 1530, 790, PosterBack, 2);
            if (t == null) { TC("THIS TOURNAMENT IS DAMAGED", 1160, 400, 30, Bad, 600); return; }
            Tilt(TC($"<noparse>{t.name.ToUpperInvariant()}</noparse>", 1160, 216, 56, UiArt.Gold, 680), -1f);
            string by = e.mine ? "MADE BY YOU" : e.creator.Length > 0 ? "BY " + e.creator.ToUpperInvariant() : "FROM A CODE";
            TC($"<noparse>{(t.Fixed ? "FIXED LOADOUT" : "NORMAL DRAFT")}  ·  {t.teams.Length} {(t.teams.Length == 1 ? "MATCH" : "MATCHES")}  ·  {by}</noparse>", 1160, 262, 22, Pale, 690);
            Ladder(t, 296, 620);
            var v = Tournaments.VerifyOf(e.verify, e.Hash);
            string line = $"ESTIMATED CLEAR CHANCE {Tournaments.Pct(Tournaments.ClearChance(t))}";
            if (e.mine) line += v != null ? $"  ·  YOU CLEARED IT IN {RunClock.Format(v.seconds)}" : "  ·  BEAT IT ONCE TO PUBLISH IT";
            else if (e.best > 0f) line += "  ·  YOUR BEST " + RunClock.Format(e.best);
            TC(line, 1160, 638, 21, e.mine && v == null ? UiArt.Gold : Green, 700);

            // actions: PLAY (or CONTINUE), then icon buttons
            var saved = RunSaves.CommunityRun;
            bool cont = saved.has && saved.community == e.Hash;
            var icons = new List<(string icon, string label, Action act)>();
            if (cont) icons.Add(("refresh", "RESTART", () => Play(e)));
            if (e.mine) icons.Add(("pencil", "EDIT", () => { Destroy(gameObject); TournamentEditor.Open(e); }));
            icons.Add(("share", "CODE", () => CopyCode(e)));
            if (e.mine && Community.Configured) icons.Add(("globe", e.remote.Length == 0 ? "PUBLISH" : e.published == e.Hash ? "ONLINE" : "UPDATE", () => Publish(e)));
            icons.Add(("bin", confirmDelete ? "SURE?" : "DELETE", () => Delete(e)));
            float iw = 76f, gap = 10f, x1 = 1520f - icons.Count * (iw + gap) + gap;
            var play = Rect("Play", 830, 662, x1 - 18, 742, UiArt.Gold, 5, true);
            AddStop(play, cont ? (Action)(() => Continue(saved)) : () => Play(e));
            Clickable(play, cont ? (Action)(() => Continue(saved)) : () => Play(e), UiArt.Gold, new Color(1f, 0.9f, 0.5f, 1f));
            Tilt(play, -0.8f);
            Icon("play", P(872, 702), 1.5f, UiArt.Dark, 8);
            string playLabel = cont ? $"CONTINUE {Mathf.Clamp(saved.gameNumber - (9 - t.teams.Length) + 1, 1, t.teams.Length)}/{t.teams.Length}" : e.mine && v == null ? "TEST PLAY" : "PLAY";
            Tilt(TC(playLabel, (900 + x1 - 18) / 2f, 704, playLabel.Length > 6 ? 36 : 50, UiArt.Dark, x1 - 940), -0.8f);
            float x = x1;
            foreach (var (icon, label, act) in icons)
            {
                var b = Btn("", x, 662, x + iw, 742, act);
                if (label == "SURE?") Shade(b, new Color(0.85f, 0.25f, 0.2f, 1f));
                Icon(icon, P(x + iw / 2f, 690), 1.1f, UiArt.Ink, 8);
                TC(label, x + iw / 2f, 726, 16, UiArt.Ink, iw);
                x += iw + gap;
            }
            if (e.mine && e.remote.Length > 0) { var rm = Btn("", 1340, 754, 1520, 782, () => Unpublish(e), 16); TC("REMOVE ONLINE", 1430, 768, 16, UiArt.Ink, 180); }
        }

        // a drawn box that clicks (the PLAY sign is a box, not a kit Button, so it can carry its own colours and tilt)
        private static void Clickable(SpriteRenderer box, Action act, Color normal, Color hover)
        {
            var col = box.gameObject.AddComponent<BoxCollider2D>();
            col.size = box.size;
            var w = box.gameObject.AddComponent<CreatorWidget>();
            w.Box = box; w.Normal = UiArt.Paint(normal); w.Hover = UiArt.Paint(hover); w.OnClick = act;
        }

        private void Select(int i) { selected = i; confirmDelete = false; status = ""; Build(); }

        public static void CopyCode(TourEntry e)
        {
            var code = Tournaments.Code(e);
            GUIUtility.systemCopyBuffer = code;
            status = $"CODE COPIED ({code.Length} CHARACTERS){(Tournaments.Verified(e) ? "" : ": NOT CLEARED BY ITS MAKER YET")}";
            Plugin.Log.LogInfo($"Tournament code copied ({code.Length} chars, {e.Hash})");
            open?.Build();
        }

        private void AddCode()
        {
            string code = GUIUtility.systemCopyBuffer ?? "";
            var why = Tournaments.Decode(code, out var t, out var entry);
            if (why != null) { status = code.Trim().Length == 0 ? "COPY A TOURNAMENT CODE FIRST, THEN PRESS ADD CODE" : why; Build(); return; }
            var have = Tournaments.Find(entry.Hash);
            var added = Tournaments.Add(entry);
            online = false;
            selected = Tournaments.All.IndexOf(added); page = selected / Cards;
            status = have != null ? "ALREADY IN YOUR LIST" : $"ADDED: {t.name.ToUpperInvariant()}";
            Plugin.Log.LogInfo($"Tournament code added: {t.name} ({entry.Hash}, verified {Tournaments.Verified(added)})");
            Build();
        }

        private void Delete(TourEntry e)
        {
            if (!confirmDelete) { confirmDelete = true; status = "PRESS DELETE AGAIN TO REMOVE IT FROM THIS PC"; Build(); return; }
            confirmDelete = false;
            if (RunSaves.CommunityRun.has && RunSaves.CommunityRun.community == e.Hash) RunSaves.ClearCommunity();
            Tournaments.Remove(e);
            status = e.mine && e.remote.Length > 0 ? "DELETED HERE (IT STAYS ONLINE: REMOVE ONLINE FIRST TO TAKE IT DOWN)" : "DELETED";
            Build();
        }

        private void Publish(TourEntry e)
        {
            var t = Tournaments.Content(e);
            var why = t != null ? Tournaments.Problem(t) : "THIS TOURNAMENT IS DAMAGED";
            if (why == null && !Tournaments.Verified(e)) why = "BEAT IT ONCE YOURSELF FIRST (PLAY IT AND WIN THE FINAL)";
            if (why == null && e.remote.Length > 0 && e.published == e.Hash) why = "THE ONLINE VERSION IS ALREADY THIS ONE";
            if (why != null) { status = why; Build(); return; }
            status = "PUBLISHING..."; Build();
            bool update = e.remote.Length > 0;
            TournamentNet.Publish(e, err => { status = err ?? (update ? "UPDATED ONLINE (ITS PLAYS AND FASTEST CLEARS START AGAIN)" : "PUBLISHED: IT'S IN BROWSE ONLINE"); if (this != null) Build(); });
        }

        private void Unpublish(TourEntry e)
        {
            if (!confirmDelete) { confirmDelete = true; status = "PRESS REMOVE ONLINE AGAIN TO TAKE IT DOWN FOR EVERYONE"; Build(); return; }
            confirmDelete = false;
            TournamentNet.Unpublish(e, err => { status = err ?? "REMOVED ONLINE (IT STAYS IN YOUR LIST)"; if (this != null) Build(); });
        }

        // ------------------------------------------------------------ browse online

        private class Row { public string id, name, creator, owner, mode, mod; public int matches, plays, clears, ratings; public float difficulty, rating; }
        private static readonly List<Row> rows = new List<Row>();
        private static int sortIdx, modeIdx, onlineSel;
        private static bool mineOnly, more, loading;
        private static string loadError;
        private static TourReply detail;
        private static Tournament detailT;
        private static TourScores scores;
        private static readonly string[] Modes = { null, "fixed", "draft" };
        private static readonly string[] ModeNames = { "ALL", "FIXED", "DRAFT" };

        private void Refresh()
        {
            rows.Clear(); more = false; page = 0; onlineSel = 0; detail = null; detailT = null; scores = null; loadError = null;
            Build();
            if (!Community.Configured) return;
            LoadPage(0);
        }

        private void LoadPage(int offset)
        {
            loading = true;
            TournamentNet.List(TournamentNet.Sorts[sortIdx], Modes[modeIdx], mineOnly, offset, (r, err) =>
            {
                loading = false;
                if (err != null || r == null || r.ids == null) { loadError = err ?? "NO ANSWER"; if (this != null) Build(); return; }
                for (int i = 0; i < r.ids.Length; i++)
                    rows.Add(new Row { id = r.ids[i], name = r.names[i], creator = r.creators[i], owner = r.owners[i], mode = r.modes[i], mod = r.mods != null && i < r.mods.Length ? r.mods[i] : "",
                                       matches = r.matches[i], plays = r.plays[i], clears = r.clears[i], ratings = r.ratings[i], difficulty = r.difficulty[i], rating = r.rating[i] });
                more = r.ids.Length >= Generated.Community.PageSize;
                if (this == null) return;
                if (offset == 0 && rows.Count > 0) SelectOnline(0); else Build();
            });
        }

        private void SelectOnline(int i)
        {
            onlineSel = i; confirmReport = false; detail = null; detailT = null; scores = null; status = "";
            Build();
            var id = rows[i].id;
            TournamentNet.Fetch(id, (r, err) =>
            {
                if (this == null || onlineSel >= rows.Count || rows[onlineSel].id != id) return;
                detail = r;
                if (r != null && Tournaments.Decode(r.code, out var t, out _) == null) detailT = t;
                else status = err ?? "THIS TOURNAMENT DOESN'T WORK WITH YOUR VERSION OF THE MOD";
                Build();
            });
            TournamentNet.Scores(id, (s, err) => { if (this != null && onlineSel < rows.Count && rows[onlineSel].id == id) { scores = s; Build(); } });
        }

        private void BuildOnline()
        {
            if (!Community.Configured)
            {
                TC("NOT CONNECTED", 800, 400, 44, UiArt.Ink, 900);
                TC("COMMUNITY FEATURES ARE OFF. TOURNAMENTS CAN STILL BE SHARED AS CODES (MY TOURNAMENTS).", 800, 460, 24, Pale, 1200);
                return;
            }
            // sort and filter
            float fx = 70, fw = 165;
            Btn("", fx, 160, fx + fw, 196, () => { sortIdx = (sortIdx + 1) % TournamentNet.Sorts.Length; Refresh(); });
            TC("SORT: " + TournamentNet.SortNames[sortIdx], fx + fw / 2f, 178, 19, UiArt.Ink, fw);
            Btn("", fx + fw + 10, 160, fx + 2 * fw + 10, 196, () => { modeIdx = (modeIdx + 1) % 3; Refresh(); });
            TC("TYPE: " + ModeNames[modeIdx], fx + fw * 1.5f + 10, 178, 19, UiArt.Ink, fw);
            var mineB = Btn("", fx + 2 * fw + 20, 160, fx + 3 * fw + 20, 196, () => { mineOnly = !mineOnly; Refresh(); });
            if (mineOnly) Shade(mineB, new Color(1f, 0.6f, 0.2f, 1f));
            TC(mineOnly ? "ONLY MINE" : "EVERYONE'S", fx + fw * 2.5f + 20, 178, 19, UiArt.Ink, fw);
            Btn("", 595, 160, 760, 196, Refresh);
            Icon("refresh", P(620, 178), 0.8f, UiArt.Ink, 7);
            TC("REFRESH", 690, 178, 19, UiArt.Ink, 120);

            if (rows.Count == 0)
                Text(loading ? "LOADING..." : loadError != null ? "COULDN'T REACH THE SERVER: " + loadError : "NOTHING PUBLISHED HERE YET", P(415, 420), F(30), 620 / 25f * KX, UiArt.Ink, height: 3f, wrap: true);
            int pages = Math.Max(1, (rows.Count + Cards - 1) / Cards);
            page = Mathf.Clamp(page, 0, pages - 1);
            float y = 210;
            for (int i = page * Cards; i < Math.Min(rows.Count, (page + 1) * Cards); i++)
            {
                OnlineCard(rows[i], i, y);
                y += 192;
            }
            Pager(pages + (more ? 1 : 0), 805);
            if (rows.Count > 0) OnlinePoster(rows[Mathf.Clamp(onlineSel, 0, rows.Count - 1)]);
        }

        private void OnlineCard(Row r, int i, float y)
        {
            bool sel = i == onlineSel;
            var txt = sel ? UiArt.Dark : UiArt.Ink;
            Shade(Btn("", 70, y, 760, y + 176, () => SelectOnline(i)), sel ? new Color(1f, 0.63f, 0.24f, 1f) : CardBlue);
            bool have = Tournaments.All.Any(e => e.remote == r.id);
            TL($"<noparse>{r.name.ToUpperInvariant()}</noparse>", 96, y + 34, 38, txt, 440);
            Badges(98, y + 77, (r.mode.ToUpperInvariant(), r.mode == "fixed" ? Sky : Coral), ($"{r.matches} {(r.matches == 1 ? "MATCH" : "MATCHES")}", UiArt.Ink), ("BY " + r.creator.ToUpperInvariant(), UiArt.Gold), (have ? "IN YOUR LIST" : null, Green));
            TL($"v{r.mod}  ·  {r.plays} {(r.plays == 1 ? "PLAYER" : "PLAYERS")}{(r.plays > 0 ? $"  ·  {Mathf.RoundToInt(100f * r.clears / r.plays)}% CLEARED" : "")}", 98, y + 128, 22, txt, 460);
            if (r.ratings > 0)
            {
                Icon("star", P(110, y + 156), 0.8f, UiArt.Gold, 8);
                TL($"{r.rating:0.0}  ({r.ratings} {(r.ratings == 1 ? "RATING" : "RATINGS")})", 128, y + 156, 20, txt, 300);
            }
            else TL("NOT RATED YET", 98, y + 156, 20, txt, 300);
            int diff = (int)r.difficulty;
            TR("DIFFICULTY", 660, y + 36, 18, txt, 120);
            TC($"{diff}/10", 705, y + 34, 30, txt, 80);
            Flames(546, y + 72, diff, sel);
        }

        private void OnlinePoster(Row r)
        {
            Rect("Poster", 790, 170, 1530, 790, PosterBack, 2);
            Tilt(TC($"<noparse>{r.name.ToUpperInvariant()}</noparse>", 1160, 214, 52, UiArt.Gold, 680), -1f);
            TC($"<noparse>BY {r.creator.ToUpperInvariant()}  ·  MADE WITH v{r.mod}  ·  {(r.mode == "fixed" ? "FIXED LOADOUT" : "NORMAL DRAFT")}</noparse>", 1160, 256, 21, Pale, 690);
            if (detail == null) { TC("LOADING...", 1160, 420, 30, Pale, 400); return; }
            if (!string.IsNullOrEmpty(detail.description)) TC($"<noparse>{detail.description}</noparse>", 1160, 284, 19, Pale, 690);
            if (detailT != null) Ladder(detailT, 304, 528);
            // fastest clears
            TL("FASTEST CLEARS", 830, 552, 22, UiArt.Gold, 300);
            if (scores == null || scores.ranks == null || scores.ranks.Length == 0) TL(scores == null ? "LOADING..." : "NO CLEARS YET", 830, 584, 20, Pale, 360);
            else
                for (int i = 0; i < Math.Min(3, scores.ranks.Length); i++)
                {
                    bool me = scores.ids[i] == Identity.Id;
                    TL($"#{scores.ranks[i]}  <noparse>{scores.names[i]}</noparse>  ·  {RunClock.Format(scores.values[i])}", 830, 582 + i * 26, 20, me ? UiArt.Gold : UiArt.Ink, 380);
                }
            if (scores != null && scores.meRank > 3) TL($"YOU: #{scores.meRank} · {RunClock.Format(scores.meValue)}", 830, 660, 18, UiArt.Gold, 380);
            // rating
            TC("RATE IT", 1380, 552, 22, UiArt.Gold, 200);
            for (int s = 1; s <= 5; s++)
            {
                int stars = s;
                float sx = 1236 + (s - 1) * 58;
                Btn("", sx, 572, sx + 50, 618, () => Rate(r, stars));
                Icon("star", P(sx + 25, 595), 0.95f, UiArt.Gold, 8);
                TC(s.ToString(), sx + 39, 607, 14, UiArt.Ink, 20);
            }
            // actions
            var have = Tournaments.All.FirstOrDefault(e => e.remote == r.id || detail.hash != null && e.Hash == detail.hash);
            bool mineOnline = r.owner == Identity.Id;
            if (detailT != null)
            {
                var play = Rect("Play", 830, 662, 1230, 742, UiArt.Gold, 5, true);
                Action go = () => { var e = AddOnline(r); if (e != null) Play(e); };
                AddStop(play, go);
                Clickable(play, go, UiArt.Gold, new Color(1f, 0.9f, 0.5f, 1f));
                Tilt(play, -0.8f);
                Icon("play", P(872, 702), 1.5f, UiArt.Dark, 8);
                Tilt(TC("PLAY", 1060, 704, 50, UiArt.Dark, 300), -0.8f);
                var add = Btn("", 1250, 662, 1326, 742, () => { if (AddOnline(r) != null) { status = "ADDED TO MY TOURNAMENTS"; Build(); } });
                Icon(have != null ? "check" : "plus", P(1288, 690), 1.0f, UiArt.Ink, 8);
                TC(have != null ? "IN LIST" : "ADD", 1288, 726, 16, UiArt.Ink, 76);
            }
            var rep = Btn("", 1336, 662, 1520, 742, () => { if (mineOnline) { var mine = Tournaments.All.FirstOrDefault(e => e.remote == r.id && e.mine); if (mine != null) Unpublish(mine); else UnpublishRemote(r); } else Report(r); });
            if (confirmReport) Shade(rep, new Color(0.85f, 0.25f, 0.2f, 1f));
            TC(mineOnline ? "REMOVE ONLINE" : confirmReport ? "SURE? REPORT" : "REPORT", 1428, 694, 22, UiArt.Ink, 180);
            TC(mineOnline ? "(YOURS)" : "OFFENSIVE / BROKEN", 1428, 724, 15, Pale, 180);
        }

        // the selected online tournament in this PC's list (or the entry it already has)
        private TourEntry AddOnline(Row r)
        {
            if (detail == null || detailT == null) return null;
            if (Tournaments.Decode(detail.code, out _, out var entry) != null) return null;
            entry.remote = r.id; entry.creator = r.creator; entry.published = entry.Hash; entry.mine = r.owner == Identity.Id;
            var have = Tournaments.Find(entry.Hash);
            if (have != null) { have.remote = r.id; have.published = entry.Hash; if (!have.mine) have.creator = r.creator; Tournaments.Save(); return have; }
            // an older version of the same online tournament: replaced by this one (records start again with the new version)
            var old = Tournaments.All.FirstOrDefault(e => e.remote == r.id && !e.mine);
            if (old != null) Tournaments.Remove(old);
            Plugin.Log.LogInfo($"Tournament added from the browser: {r.name} ({r.id}, {entry.Hash})");
            return Tournaments.Add(entry);
        }

        private void Rate(Row r, int stars)
        {
            status = "RATING..."; Build();
            TournamentNet.Rate(r.id, stars, err => { status = err ?? $"RATED {stars} {(stars == 1 ? "STAR" : "STARS")}"; if (this != null) Build(); });
        }

        private void Report(Row r)
        {
            if (!confirmReport) { confirmReport = true; status = "REPORT IT IF IT'S OFFENSIVE OR BROKEN: PRESS AGAIN TO SEND"; Build(); return; }
            confirmReport = false;
            TournamentNet.Report(r.id, err => { status = err ?? "REPORTED: THANKS. ENOUGH REPORTS HIDE IT UNTIL IT'S CHECKED"; if (this != null) Build(); });
        }

        private void UnpublishRemote(Row r)
        {
            var e = new TourEntry { remote = r.id };
            TournamentNet.Unpublish(e, err => { status = err ?? "REMOVED ONLINE"; if (this != null) Refresh(); });
        }

        // ------------------------------------------------------------ playing

        private static TitleButton ClassicButton() =>
            FindObjectsByType<TitleButton>(FindObjectsSortMode.None).FirstOrDefault(b => !ContinueMenu.IsInfinite(b) && ContinueMenu.Players(b) == 1);

        public static void Play(TourEntry e, bool test = false)
        {
            var tb = ClassicButton();
            if (tb == null || Tournaments.Content(e) == null) return;
            Tournaments.Pending = e;
            Tournaments.PendingTest = test || (e.mine && !Tournaments.Verified(e));
            ContinueMenu.Bypass = true;
            if (open != null) Destroy(open.gameObject);
            if (TournamentEditor.IsOpen) TournamentEditor.CloseNow();
            GameManager.Instance.numberOfPlayers = 1;
            tb.Click();
        }

        private void Continue(RunSnapshot snap)
        {
            var tb = ClassicButton();
            if (tb == null) return;
            Destroy(gameObject);
            ContinueMenu.Resume(tb, snap);
        }

        private void Update()
        {
            if (NavUpdate()) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame) { Destroy(gameObject); return; }
                if (!online && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed) && kb.vKey.wasPressedThisFrame) { AddCode(); return; }
            }
            // pad shortcuts on MY TOURNAMENTS: X deletes, Y copies the code
            if (online || Tournaments.All.Count == 0) return;
            foreach (var g in UnityEngine.InputSystem.Gamepad.all)
            {
                var e = Tournaments.All[Mathf.Clamp(selected, 0, Tournaments.All.Count - 1)];
                if (g.buttonWest.wasPressedThisFrame) { Delete(e); return; }
                if (g.buttonNorth.wasPressedThisFrame) { CopyCode(e); return; }
            }
        }

        protected override void OnNavBack() => Destroy(gameObject);
        protected override void OnNavTab(int dir) => SetTab(!online);
        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }

        // "FIXED · 5 MATCHES · DIFFICULTY 6/10 · BY YOU · CLEARED 1/4 · BEST 4:12.3" (other screens' one-line summary)
        public static string Line(TourEntry e, Tournament t)
        {
            var bits = new List<string> { t.Fixed ? "FIXED" : "DRAFT", $"{t.teams.Length} {(t.teams.Length == 1 ? "MATCH" : "MATCHES")}", $"DIFFICULTY {Tournaments.Difficulty(t)}/10" };
            bits.Add(e.mine ? "BY YOU" : e.creator.Length > 0 ? "BY " + e.creator.ToUpperInvariant() : "FROM A CODE");
            if (e.attempts > 0) bits.Add($"CLEARED {e.clears}/{e.attempts}");
            if (e.best > 0f) bits.Add("BEST " + RunClock.Format(e.best));
            return $"<noparse>{string.Join("  ·  ", bits)}</noparse>";
        }
    }
}

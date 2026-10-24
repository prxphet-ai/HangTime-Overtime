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
    // TOURNAMENTS (title bar). MY TOURNAMENTS: this PC's community tournaments (made here, added from a code or from the
    // browser), each with your record on it; PLAY / CONTINUE, EDIT (your own), COPY CODE, DELETE, and for your own cleared
    // ones PUBLISH / UPDATE / REMOVE ONLINE; NEW TOURNAMENT opens the editor; ADD CODE reads a code from the clipboard (a pad
    // can do it too). BROWSE ONLINE: the published tournaments (sorted and filtered), each with its creator, version,
    // difficulty, players, clear rate, rating and fastest clears; PLAY / ADD TO MY LIST, RATE 1-5, REPORT. LB / RB: tabs.
    internal class TournamentMenu : OvertimePanel
    {
        private static TournamentMenu open;
        public static bool IsOpen => open != null;
        private static int page, selected;
        private static string status = "";
        private static bool online;
        private bool confirmDelete, confirmReport;
        private const int Rows = 7, OnlineRows = 6;

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
        public static readonly Color Gold = new Color(1f, 0.85f, 0.35f), Pale = new Color(0.85f, 0.95f, 1f), Bad = new Color(1f, 0.55f, 0.5f);
        private float PW => W * 0.94f;
        private float PH => H * 0.92f;

        private void Build()
        {
            int keep = focus;
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops();
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            float pw = PW, ph = PH, top = ph / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            Text("COMMUNITY TOURNAMENTS", new Vector2(0f, top - 1.6f), 1.0f, pw - 2f, Color.white);
            string[] tabs = { "MY TOURNAMENTS", "BROWSE ONLINE" };
            for (int i = 0; i < 2; i++)
            {
                bool on = online == (i == 1), browse = i == 1;
                var b = Button(tabs[i], new Vector2((i - 0.5f) * pw * 0.3f, top - 3.4f), new Vector2(pw * 0.28f, 1.8f), () => SetTab(browse), 0.5f);
                Shade(b, on ? new Color(0.96f, 0.55f, 0.12f, 1f) : new Color(0.16f, 0.6f, 0.86f, 1f));
            }
            if (online) BuildOnline(); else BuildLocal();
            float by = -ph / 2f + 1.8f;
            Text($"<noparse>{status}</noparse>", new Vector2(pw * 0.14f, by), 0.4f, pw * 0.22f, Gold, height: 2.2f, wrap: true);
            Button("BACK", new Vector2(pw * 0.36f, by), new Vector2(pw * 0.16f, 2.2f), () => Destroy(gameObject), 0.6f);
            PadHint("A: CHOOSE · B: BACK · LB / RB: TABS", new Vector2(0f, by + 1.7f), pw - 2f);
            navKeys = true;
            focus = keep >= 0 && keep < stops.Count ? keep : 0;
        }

        private void SetTab(bool on)
        {
            if (online == on) return;
            online = on; status = ""; focus = -1; confirmDelete = confirmReport = false;
            if (online) Refresh(); else Build();
        }

        // ------------------------------------------------------------ my tournaments

        private void BuildLocal()
        {
            float pw = PW, ph = PH, top = ph / 2f, left = -pw / 2f;
            var list = Tournaments.All;
            int pages = Math.Max(1, (list.Count + Rows - 1) / Rows);
            page = Mathf.Clamp(page, 0, pages - 1);
            selected = Mathf.Clamp(selected, 0, Math.Max(0, list.Count - 1));
            float lw = pw * 0.52f, lx = left + 1f + lw / 2f, rowH = 2.75f, y = top - 5.8f;
            if (list.Count == 0)
                Text("NO TOURNAMENTS YET.\nNEW TOURNAMENT MAKES ONE; ADD CODE PASTES ONE A FRIEND SENT YOU; BROWSE ONLINE HAS EVERYONE'S.", new Vector2(lx, y - 6f), 0.55f, lw - 1f, Color.white, height: 4f, wrap: true);
            for (int i = page * Rows; i < Math.Min(list.Count, (page + 1) * Rows); i++)
            {
                var e = list[i];
                var t = Tournaments.Content(e);
                int idx = i;
                Shade(Button("", new Vector2(lx, y), new Vector2(lw, rowH - 0.25f), () => Select(idx), 0.5f), i == selected ? new Color(0.85f, 0.45f, 0.08f, 1f) : ListColor);
                string name = t != null ? t.name.ToUpperInvariant() : "(DAMAGED)";
                Text($"<noparse>{name}</noparse>", new Vector2(lx, y + 0.5f), 0.55f, lw - 1.2f, Color.white, TextAlignmentOptions.Left);
                Text(t == null ? "" : Line(e, t), new Vector2(lx, y - 0.6f), 0.38f, lw - 1.2f, Pale, TextAlignmentOptions.Left);
                y -= rowH;
            }
            Pager(pages, lx, top - 5.8f - rowH * Rows - 0.2f, d => { page = (page + d + pages) % pages; Build(); });

            float dx = left + 1f + lw + 1f, dw = pw - lw - 3f, cx = dx + dw / 2f;
            if (list.Count > 0) { DetailBox(cx, dw); Detail(list[selected], cx, dw, top - 5.8f); }

            float by = -ph / 2f + 1.8f;
            Button("NEW TOURNAMENT", new Vector2(-pw * 0.3f, by), new Vector2(pw * 0.2f, 2.2f), () => { Destroy(gameObject); TournamentEditor.Open(null); }, 0.5f);
            Button("ADD CODE (PASTE)", new Vector2(-pw * 0.08f, by), new Vector2(pw * 0.2f, 2.2f), AddCode, 0.5f);
        }

        private void DetailBox(float cx, float dw)
        {
            float top = PH / 2f - 4.6f, bottom = -PH / 2f + 4.2f;
            Box("Detail", new Vector2(cx, (top + bottom) / 2f), new Vector2(dw, top - bottom), new Color(0.02f, 0.2f, 0.34f, 1f), 2);
        }

        private void Pager(int pages, float x, float y, Action<int> step)
        {
            if (pages <= 1) return;
            Button("<", new Vector2(x - 3f, y), new Vector2(2f, 1.6f), () => step(-1), 0.6f);
            Text($"PAGE {page + 1}/{pages}", new Vector2(x, y), 0.45f, 6f, Pale);
            Button(">", new Vector2(x + 3f, y), new Vector2(2f, 1.6f), () => step(1), 0.6f);
        }

        // "FIXED · 5 MATCHES · DIFFICULTY 6/10 · BY YOU · CLEARED 1/4 · BEST 4:12.3"
        public static string Line(TourEntry e, Tournament t)
        {
            var bits = new List<string> { t.Fixed ? "FIXED" : "DRAFT", $"{t.teams.Length} {(t.teams.Length == 1 ? "MATCH" : "MATCHES")}", $"DIFFICULTY {Tournaments.Difficulty(t)}/10" };
            bits.Add(e.mine ? "BY YOU" : e.creator.Length > 0 ? "BY " + e.creator.ToUpperInvariant() : "FROM A CODE");
            if (e.remote.Length > 0) bits.Add("ONLINE");
            if (e.attempts > 0) bits.Add($"CLEARED {e.clears}/{e.attempts}");
            if (e.best > 0f) bits.Add("BEST " + RunClock.Format(e.best));
            return $"<noparse>{string.Join("  ·  ", bits)}</noparse>";
        }

        // the matches of a tournament, one line each
        private float Matches(Tournament t, float cx, float dw, float y)
        {
            for (int i = 0; i < t.teams.Length; i++)
            {
                var tm = t.teams[i];
                Text($"<noparse>{i + 1}. {tm.name.ToUpperInvariant()}</noparse>  <size=80%><color=#BFE6FF>{tm.@base.ToUpperInvariant()} · {tm.perks.Length}P {tm.cards.Length}C · WIN ~{Tournaments.Pct(Tournaments.WinChance(t, i))}</color></size>",
                     new Vector2(cx, y), 0.42f, dw - 1f, Color.white);
                y -= 0.95f;
            }
            return y;
        }

        private void Detail(TourEntry e, float cx, float dw, float y)
        {
            var t = Tournaments.Content(e);
            if (t == null) { Text("THIS TOURNAMENT IS DAMAGED", new Vector2(cx, y), 0.55f, dw - 1f, Bad); return; }
            Text($"<noparse>{t.name.ToUpperInvariant()}</noparse>", new Vector2(cx, y), 0.7f, dw - 1f, Color.white);
            y -= 1.1f;
            Text(Line(e, t), new Vector2(cx, y), 0.36f, dw - 1f, Gold);
            y -= 1.0f;
            if (t.desc.Length > 0) { Text($"<noparse>{t.desc}</noparse>", new Vector2(cx, y - 0.6f), 0.4f, dw - 1.4f, Pale, height: 2f, wrap: true); y -= 2.2f; }
            y = Matches(t, cx, dw, y);
            if (t.Fixed) { Text($"YOUR TEAM: {t.player.perks.Length} PERKS · {t.player.cards.Length} CARDS · {t.player.levels.Sum()} STAT LEVELS", new Vector2(cx, y - 0.2f), 0.4f, dw - 1f, Pale); y -= 1.1f; }
            Text($"ESTIMATED CLEAR CHANCE {Tournaments.Pct(Tournaments.ClearChance(t))} (SIMULATOR)", new Vector2(cx, y - 0.2f), 0.4f, dw - 1f, Pale);
            if (e.mine)
            {
                var v = Tournaments.VerifyOf(e.verify, e.Hash);
                string online = e.remote.Length == 0 ? "" : e.published == e.Hash ? "  ·  ONLINE" : "  ·  CHANGED SINCE PUBLISHING";
                Text((v != null ? $"YOU CLEARED IT IN {RunClock.Format(v.seconds)}: READY TO PUBLISH" : "BEAT IT ONCE YOURSELF BEFORE IT CAN BE PUBLISHED") + online,
                     new Vector2(cx, y - 1.2f), 0.4f, dw - 1f, v != null ? new Color(0.5f, 1f, 0.55f) : Gold);
            }

            // actions: play, edit / code / delete, online
            float ab = -PH / 2f + 5.5f, bw = (dw - 2f) / 2f;
            var saved = RunSaves.CommunityRun;
            if (saved.has && saved.community == e.Hash)
            {
                Button($"CONTINUE (MATCH {Mathf.Clamp(saved.gameNumber - (9 - t.teams.Length) + 1, 1, t.teams.Length)}/{t.teams.Length})", new Vector2(cx - bw / 2f - 0.25f, ab + 4.6f), new Vector2(bw, 2f), () => Continue(saved), 0.42f);
                Button("PLAY FROM THE START", new Vector2(cx + bw / 2f + 0.25f, ab + 4.6f), new Vector2(bw, 2f), () => Play(e), 0.42f);
            }
            else Button(e.mine && !Tournaments.Verified(e) ? "TEST PLAY" : "PLAY", new Vector2(cx, ab + 4.6f), new Vector2(dw - 2f, 2f), () => Play(e), 0.6f);
            int n = e.mine ? 3 : 2;
            float w3 = (dw - 2f - (n - 1) * 0.5f) / n, x = cx - (dw - 2f) / 2f + w3 / 2f;
            if (e.mine) { Button("EDIT", new Vector2(x, ab + 2.3f), new Vector2(w3, 1.9f), () => { Destroy(gameObject); TournamentEditor.Open(e); }, 0.5f); x += w3 + 0.5f; }
            Button("COPY CODE", new Vector2(x, ab + 2.3f), new Vector2(w3, 1.9f), () => CopyCode(e), 0.5f); x += w3 + 0.5f;
            var del = Button(confirmDelete ? "SURE? DELETE" : "DELETE", new Vector2(x, ab + 2.3f), new Vector2(w3, 1.9f), () => Delete(e), 0.45f);
            if (confirmDelete) Shade(del, new Color(0.85f, 0.25f, 0.2f, 1f));
            if (e.mine && Community.Configured)
            {
                bool verified = Tournaments.Verified(e), up = e.remote.Length > 0;
                if (!up) Button(verified ? "PUBLISH ONLINE" : "PUBLISH ONLINE (BEAT IT FIRST)", new Vector2(cx, ab), new Vector2(dw - 2f, 1.9f), () => Publish(e), 0.45f);
                else
                {
                    Button(e.published == e.Hash ? "UP TO DATE ONLINE" : verified ? "UPDATE ONLINE" : "UPDATE ONLINE (BEAT IT FIRST)", new Vector2(cx - bw / 2f - 0.25f, ab), new Vector2(bw, 1.9f), () => Publish(e), 0.42f);
                    Button("REMOVE ONLINE", new Vector2(cx + bw / 2f + 0.25f, ab), new Vector2(bw, 1.9f), () => Unpublish(e), 0.42f);
                }
            }
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
            selected = Tournaments.All.IndexOf(added); page = selected / Rows;
            status = have != null ? "ALREADY IN YOUR LIST" : $"ADDED: {t.name.ToUpperInvariant()}";
            Plugin.Log.LogInfo($"Tournament code added: {t.name} ({entry.Hash}, verified {Tournaments.Verified(added)})");
            Build();
        }

        private void Delete(TourEntry e)
        {
            if (!confirmDelete) { confirmDelete = true; Build(); return; }
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

        private static string RowLine(Row r) =>
            $"<noparse>BY {r.creator.ToUpperInvariant()}  ·  v{r.mod}  ·  {r.mode.ToUpperInvariant()}  ·  {r.matches} {(r.matches == 1 ? "MATCH" : "MATCHES")}  ·  DIFFICULTY {(int)r.difficulty}/10  ·  " +
            $"{r.plays} {(r.plays == 1 ? "PLAYER" : "PLAYERS")}{(r.plays > 0 ? $"  ·  {Mathf.RoundToInt(100f * r.clears / r.plays)}% CLEARED" : "")}  ·  {(r.ratings > 0 ? $"{r.rating:0.0} STARS ({r.ratings})" : "NOT RATED")}</noparse>";

        private void BuildOnline()
        {
            float pw = PW, ph = PH, top = ph / 2f, left = -pw / 2f;
            float lw = pw * 0.52f, lx = left + 1f + lw / 2f, fy = top - 5.6f;
            if (!Community.Configured)
            {
                Text("NOT CONNECTED: THE COMMUNITY SERVER ISN'T SET UP YET.\nTOURNAMENTS CAN STILL BE SHARED AS CODES (MY TOURNAMENTS > COPY CODE / ADD CODE).", new Vector2(0f, 0f), 0.6f, pw - 6f, Color.white, height: 4f, wrap: true);
                return;
            }
            // sort and filter
            float fw = (lw - 1.5f) / 4f, fx = lx - lw / 2f + fw / 2f;
            Button("SORT: " + TournamentNet.SortNames[sortIdx], new Vector2(fx, fy), new Vector2(fw, 1.7f), () => { sortIdx = (sortIdx + 1) % TournamentNet.Sorts.Length; Refresh(); }, 0.38f);
            Button("TYPE: " + ModeNames[modeIdx], new Vector2(fx + fw + 0.5f, fy), new Vector2(fw, 1.7f), () => { modeIdx = (modeIdx + 1) % 3; Refresh(); }, 0.38f);
            Toggle("MINE", new Vector2(fx + 2 * (fw + 0.5f), fy), new Vector2(fw, 1.7f), () => mineOnly, v => { mineOnly = v; Refresh(); }, 0.38f);
            Button("REFRESH", new Vector2(fx + 3 * (fw + 0.5f), fy), new Vector2(fw, 1.7f), Refresh, 0.38f);

            float rowH = 2.75f, y = fy - 2.4f;
            if (rows.Count == 0)
                Text(loading ? "LOADING..." : loadError != null ? "COULDN'T REACH THE SERVER: " + loadError : "NOTHING PUBLISHED HERE YET", new Vector2(lx, y - 5f), 0.55f, lw - 1f, Color.white, height: 3f, wrap: true);
            int pages = Math.Max(1, (rows.Count + OnlineRows - 1) / OnlineRows);
            page = Mathf.Clamp(page, 0, pages - 1);
            for (int i = page * OnlineRows; i < Math.Min(rows.Count, (page + 1) * OnlineRows); i++)
            {
                var r = rows[i];
                int idx = i;
                Shade(Button("", new Vector2(lx, y), new Vector2(lw, rowH - 0.25f), () => SelectOnline(idx), 0.5f), i == onlineSel ? new Color(0.85f, 0.45f, 0.08f, 1f) : ListColor);
                Text($"<noparse>{r.name.ToUpperInvariant()}</noparse>" + (Tournaments.All.Any(e => e.remote == r.id) ? "  <size=70%><color=#7CFF8A>IN YOUR LIST</color></size>" : ""), new Vector2(lx, y + 0.5f), 0.55f, lw - 1.2f, Color.white, TextAlignmentOptions.Left);
                Text(RowLine(r), new Vector2(lx, y - 0.6f), 0.34f, lw - 1.2f, Pale, TextAlignmentOptions.Left);
                y -= rowH;
            }
            float py = fy - 2.4f - rowH * OnlineRows - 0.2f;
            Pager(pages, lx, py, d =>
            {
                if (d > 0 && page == pages - 1 && more) { page++; LoadPage(rows.Count); return; }   // the next page from the server
                page = (page + d + pages) % pages; Build();
            });

            float dx = left + 1f + lw + 1f, dw = pw - lw - 3f, cx = dx + dw / 2f;
            if (rows.Count > 0) { DetailBox(cx, dw); OnlineDetail(rows[Mathf.Clamp(onlineSel, 0, rows.Count - 1)], cx, dw, top - 5.8f); }
        }

        private void OnlineDetail(Row r, float cx, float dw, float y)
        {
            Text($"<noparse>{r.name.ToUpperInvariant()}</noparse>", new Vector2(cx, y), 0.7f, dw - 1f, Color.white);
            y -= 1.1f;
            Text($"<noparse>BY {r.creator.ToUpperInvariant()}  ·  MADE WITH v{r.mod}  ·  {r.mode.ToUpperInvariant()}  ·  DIFFICULTY {(int)r.difficulty}/10</noparse>", new Vector2(cx, y), 0.36f, dw - 1f, Gold);
            y -= 0.9f;
            Text($"{r.plays} {(r.plays == 1 ? "PLAYER" : "PLAYERS")}  ·  {(r.plays > 0 ? Mathf.RoundToInt(100f * r.clears / r.plays) + "% CLEARED" : "NOBODY HAS PLAYED IT YET")}  ·  {(r.ratings > 0 ? $"{r.rating:0.0} / 5 STARS ({r.ratings})" : "NOT RATED YET")}",
                 new Vector2(cx, y), 0.36f, dw - 1f, Pale);
            y -= 1.0f;
            if (detail == null) { Text("LOADING...", new Vector2(cx, y - 1f), 0.5f, dw - 1f, Pale); return; }
            if (!string.IsNullOrEmpty(detail.description)) { Text($"<noparse>{detail.description}</noparse>", new Vector2(cx, y - 0.6f), 0.38f, dw - 1.4f, Pale, height: 2f, wrap: true); y -= 2.1f; }
            if (detailT != null) y = Matches(detailT, cx, dw, y);
            // fastest clears
            y -= 0.3f;
            Text("FASTEST CLEARS", new Vector2(cx, y), 0.42f, dw - 1f, Gold);
            y -= 0.9f;
            if (scores == null || scores.ranks == null || scores.ranks.Length == 0) { Text(scores == null ? "LOADING..." : "NO CLEARS YET", new Vector2(cx, y), 0.38f, dw - 1f, Pale); y -= 0.9f; }
            else
                for (int i = 0; i < Math.Min(4, scores.ranks.Length); i++)
                {
                    bool me = scores.ids[i] == Identity.Id;
                    Text($"#{scores.ranks[i]}  <noparse>{scores.names[i]}</noparse>  ·  {RunClock.Format(scores.values[i])}", new Vector2(cx, y), 0.38f, dw - 1f, me ? Gold : Color.white);
                    y -= 0.8f;
                }
            if (scores != null && scores.meRank > 4) Text($"YOU: #{scores.meRank}  ·  {RunClock.Format(scores.meValue)}", new Vector2(cx, y), 0.38f, dw - 1f, Gold);

            // actions
            float ab = -PH / 2f + 5.5f, bw = (dw - 2f) / 2f;
            var have = Tournaments.All.FirstOrDefault(e => e.remote == r.id || detail.hash != null && e.Hash == detail.hash);
            bool mineOnline = r.owner == Identity.Id;
            if (detailT != null)
            {
                Button("PLAY", new Vector2(cx - bw / 2f - 0.25f, ab + 4.6f), new Vector2(bw, 2f), () => { var e = AddOnline(r); if (e != null) Play(e); }, 0.6f);
                Button(have != null ? "IN YOUR LIST" : "ADD TO MY LIST", new Vector2(cx + bw / 2f + 0.25f, ab + 4.6f), new Vector2(bw, 2f), () => { if (AddOnline(r) != null) { status = "ADDED TO MY TOURNAMENTS"; Build(); } }, 0.45f);
            }
            Text("RATE IT\n<size=70%>1-5 STARS</size>",new Vector2(cx - dw / 2f + 2.2f, ab + 2.3f), 0.4f, 4f, Pale, height: 2f);
            float sw = (dw - 6f) / 5f;
            for (int s = 1; s <= 5; s++)
            {
                int stars = s;
                Button(s.ToString(), new Vector2(cx - dw / 2f + 4.6f + (s - 0.5f) * sw, ab + 2.3f), new Vector2(sw - 0.3f, 1.8f), () => Rate(r, stars), 0.42f);
            }
            if (mineOnline)
            {
                var mine = Tournaments.All.FirstOrDefault(e => e.remote == r.id && e.mine);
                Button("REMOVE ONLINE (YOURS)", new Vector2(cx, ab), new Vector2(dw - 2f, 1.9f), () => { if (mine != null) Unpublish(mine); else UnpublishRemote(r); }, 0.45f);
            }
            else
            {
                var rep = Button(confirmReport ? "SURE? REPORT IT" : "REPORT (OFFENSIVE OR BROKEN)", new Vector2(cx, ab), new Vector2(dw - 2f, 1.9f), () => Report(r), 0.42f);
                if (confirmReport) Shade(rep, new Color(0.85f, 0.25f, 0.2f, 1f));
            }
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
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame) Destroy(gameObject);
            else if (!online && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed) && kb.vKey.wasPressedThisFrame) AddCode();
        }

        protected override void OnNavBack() => Destroy(gameObject);
        protected override void OnNavTab(int dir) => SetTab(!online);
        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }
    }
}

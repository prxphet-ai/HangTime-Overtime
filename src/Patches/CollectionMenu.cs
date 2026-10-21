using System;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HangtimeOvertime.Generated;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // COLLECTION (title bar): every team, perk and stat card, found ones in their element colour, the rest as ???;
    // filters (rarity or class, element or style, found / locked), sorting, a detail pane for the highlighted entry and a
    // STATS page (wins, losses and streaks per mode, records, completion). Data: Collection (discoveries), Records.
    internal class CollectionMenu : OvertimePanel
    {
        private static CollectionMenu open;
        public static bool IsOpen => open != null;
        private static readonly string[] Tabs = { "TEAMS", "PERKS", "STAT CARDS", "STATS" };
        private const int Cols = 5, Rows = 7;

        // filter state survives closing the screen (this session)
        private static int tab, page, group, second, show, sort;
        private static readonly string[] Shows = { "ALL", "FOUND", "LOCKED" };
        private string selected;                                   // key of the entry in the detail pane
        private readonly Dictionary<SpriteRenderer, CollectionEntry> cellOf = new Dictionary<SpriteRenderer, CollectionEntry>();
        private int lastFocus = -2;
        private TextMeshPro detail;
        private float pw, ph;

        public static void Open()
        {
            if (open != null) return;
            var go = new GameObject("Overtime Collection");
            open = go.AddComponent<CollectionMenu>();
            if (!open.InitKit(200)) { Destroy(go); open = null; return; }
            if (TitleController.onTitle) open.HoldTitle();
            open.Build();
        }

        // ------------------------------------------------------------ filters per tab

        private string[] Groups => tab == 0 ? new[] { "ALL", "REGULAR", "COMBO", "BOSS" } : tab == 2 ? new[] { "ALL", "COMMON", "RARE", "EPIC", "GAME" } : new[] { "ALL", "COMMON", "RARE", "EPIC" };
        private string GroupLabel => tab == 0 ? "CLASS" : "RARITY";
        private string[] Seconds => tab == 2 ? new[] { "ALL", "BOOST", "PLAYSTYLE", "TRADEOFF", "GAME" } : new[] { "ALL" }.Concat(Elements.All.Select(e => e.Id.ToUpperInvariant())).ToArray();
        private string SecondLabel => tab == 2 ? "STYLE" : "ELEMENT";
        private string[] Sorts => tab == 0 ? new[] { "NAME", "CLASS", "ELEMENT", "MOST FACED", "MOST BEATEN" } : tab == 2 ? new[] { "NAME", "RARITY", "STYLE", "MOST PICKED" } : new[] { "NAME", "RARITY", "ELEMENT", "MOST PICKED" };

        private List<CollectionEntry> AllEntries() => tab == 0 ? Collection.TeamEntries() : tab == 1 ? Collection.PerkEntries() : Collection.CardEntries();

        private static int RarityRank(string g) => g == "epic" ? 0 : g == "rare" ? 1 : g == "common" ? 2 : g == "boss" ? 0 : g == "combo" ? 1 : g == "regular" ? 2 : 3;

        private List<CollectionEntry> Shown(List<CollectionEntry> all)
        {
            IEnumerable<CollectionEntry> q = all;
            var g = Groups[Mathf.Clamp(group, 0, Groups.Length - 1)].ToLowerInvariant();
            if (g != "all") q = q.Where(e => e.Group == g);
            var s = Seconds[Mathf.Clamp(second, 0, Seconds.Length - 1)].ToLowerInvariant();
            if (s != "all") q = q.Where(e => tab == 2 ? e.Style == s : e.Element == s);
            if (show == 1) q = q.Where(e => e.Found);
            if (show == 2) q = q.Where(e => !e.Found);
            var by = Sorts[Mathf.Clamp(sort, 0, Sorts.Length - 1)];
            var ordered = by == "RARITY" || by == "CLASS" ? q.OrderBy(e => RarityRank(e.Group)).ThenBy(e => e.Title)
                : by == "ELEMENT" ? q.OrderBy(e => e.Element).ThenBy(e => e.Title)
                : by == "STYLE" ? q.OrderBy(e => e.Style).ThenBy(e => e.Title)
                : by == "MOST PICKED" ? q.OrderByDescending(e => e.Picked).ThenByDescending(e => e.Seen).ThenBy(e => e.Title)
                : by == "MOST FACED" ? q.OrderByDescending(e => e.Seen).ThenBy(e => e.Title)
                : by == "MOST BEATEN" ? q.OrderByDescending(e => e.Beaten).ThenBy(e => e.Title)
                : q.OrderBy(e => e.Found ? 0 : 1).ThenBy(e => e.Title);   // NAME: found ones first (locked names stay hidden)
            return ordered.ToList();
        }

        // ------------------------------------------------------------ layout

        private void Build()
        {
            int keep = focus;
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops();
            cellOf.Clear();
            navKeys = true;
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            pw = W * 0.9f; ph = H * 0.9f;
            float top = ph / 2f, left = -pw / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            Text("COLLECTION", new Vector2(left + 7f, top - 1.7f), 1.1f, 12f, Color.white);
            var teams = Collection.TeamEntries(); var perks = Collection.PerkEntries(); var cards = Collection.CardEntries();
            int found = teams.Count(e => e.Found) + perks.Count(e => e.Found) + cards.Count(e => e.Found);
            int total = teams.Count + perks.Count + cards.Count;
            Text($"{found} / {total} FOUND  ·  TEAMS {teams.Count(e => e.Found)}/{teams.Count}  ·  PERKS {perks.Count(e => e.Found)}/{perks.Count}  ·  STAT CARDS {cards.Count(e => e.Found)}/{cards.Count}",
                 new Vector2(pw * 0.12f, top - 1.7f), 0.5f, pw * 0.7f, new Color(1f, 0.85f, 0.35f), TextAlignmentOptions.Right);

            float tw = pw * 0.6f / Tabs.Length;
            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                var b = Button(Tabs[i], new Vector2(-pw * 0.3f + tw * (i + 0.5f), top - 3.9f), new Vector2(tw - 0.3f, 1.9f), () => SetTab(idx), 0.55f);
                b.color = i == tab ? new Color(0.96f, 0.55f, 0.12f, 1f) : new Color(0.16f, 0.6f, 0.86f, 1f);
            }
            PadHint("A: CHOOSE   ·   B: BACK   ·   LB / RB: TABS", new Vector2(0f, top - 5.4f), pw - 2f);
            if (tab == 3) BuildStats(top, left, teams, perks, cards);
            else BuildGrid(top, left);
            Button("BACK", new Vector2(0f, -ph / 2f + 1.6f), new Vector2(pw * 0.18f, 2.2f), () => Destroy(gameObject), 0.6f);
            focus = keep >= 0 && keep < stops.Count ? keep : tab == 3 ? stops.Count - 1 : Tabs.Length + 4;
            lastFocus = -2;
        }

        private void SetTab(int t) { tab = t; page = 0; group = second = show = sort = 0; selected = null; focus = -1; Build(); }

        private void BuildGrid(float top, float left)
        {
            // filter row
            float fy = top - 6.9f, fw = pw * 0.155f;
            (string label, Action act)[] filters =
            {
                ($"{GroupLabel}: {Groups[group % Groups.Length]}", () => { group = (group + 1) % Groups.Length; page = 0; Build(); }),
                ($"{SecondLabel}: {Seconds[second % Seconds.Length]}", () => { second = (second + 1) % Seconds.Length; page = 0; Build(); }),
                ($"SHOW: {Shows[show]}", () => { show = (show + 1) % Shows.Length; page = 0; Build(); }),
                ($"SORT: {Sorts[sort % Sorts.Length]}", () => { sort = (sort + 1) % Sorts.Length; page = 0; Build(); }),
            };
            for (int i = 0; i < filters.Length; i++)
                Button(filters[i].label, new Vector2(left + 1f + fw * (i + 0.5f), fy), new Vector2(fw - 0.3f, 1.8f), filters[i].act, 0.45f);

            var entries = Shown(AllEntries());
            int perPage = Cols * Rows, pages = Mathf.Max(1, (entries.Count + perPage - 1) / perPage);
            page = Mathf.Clamp(page, 0, pages - 1);
            float gw = pw * 0.62f, cw = gw / Cols, ch = 2.05f, gy = fy - 2.5f;
            float gx = left + 1f;
            var pageEntries = entries.Skip(page * perPage).Take(perPage).ToList();
            if (selected == null || !pageEntries.Any(e => e.Key == selected)) selected = pageEntries.FirstOrDefault()?.Key;
            for (int i = 0; i < pageEntries.Count; i++)
            {
                var e = pageEntries[i];
                var at = new Vector2(gx + cw * (i % Cols + 0.5f), gy - (ch + 0.3f) * (i / Cols));
                var cell = Button("", at, new Vector2(cw - 0.3f, ch), () => { selected = e.Key; ShowDetail(); }, 0.45f);
                var tint = e.Found ? Color.Lerp(ElementColor(e.Element), new Color(0.05f, 0.25f, 0.45f), 0.45f) : new Color(0.07f, 0.24f, 0.4f, 1f);
                cell.color = tint;
                var w = cell.GetComponent<CreatorWidget>();
                if (w != null) { w.Normal = tint; w.Hover = Color.Lerp(tint, Color.white, 0.25f); }
                Text(e.Found ? e.Title.ToUpperInvariant() : "???", at + new Vector2(0f, 0.15f), 0.45f, cw - 0.6f, e.Found ? Color.white : new Color(0.6f, 0.7f, 0.8f), o: 9);
                Box("Rarity", at + new Vector2(0f, -ch / 2f + 0.28f), new Vector2(cw - 1.2f, 0.18f), GroupColor(e.Group, e.Found), 9);
                cellOf[cell] = e;
            }
            if (pageEntries.Count == 0)
                Text(show == 1 ? "NOTHING FOUND HERE YET" : "NOTHING MATCHES THESE FILTERS", new Vector2(gx + gw / 2f, gy - 4f), 0.6f, gw, new Color(0.85f, 0.95f, 1f));

            // pages
            float py = gy - (ch + 0.3f) * Rows - 0.2f;
            Button("<", new Vector2(gx + gw / 2f - 5f, py), new Vector2(2.2f, 1.8f), () => { page = (page - 1 + pages) % pages; Build(); }, 0.6f);
            Text($"PAGE {page + 1} / {pages}   ({entries.Count})", new Vector2(gx + gw / 2f, py), 0.5f, 7f, Color.white);
            Button(">", new Vector2(gx + gw / 2f + 5f, py), new Vector2(2.2f, 1.8f), () => { page = (page + 1) % pages; Build(); }, 0.6f);
            if (tab == 2 && Collection.Data.vcardTitle.Length == 0)
                Text("THE GAME'S OWN STAT CARDS JOIN THE LIST AFTER YOUR FIRST STAT-CARD UPGRADE SCREEN", new Vector2(gx + gw / 2f, py - 1.6f), 0.38f, gw, new Color(0.85f, 0.95f, 1f));

            // detail pane
            float dx = gx + gw + 0.6f, dw = pw / 2f - 1f - dx;
            Box("Detail", new Vector2(dx + dw / 2f, (fy + 1f + py - 1f) / 2f), new Vector2(dw, fy + 1f - (py - 1f)), new Color(0.02f, 0.2f, 0.34f, 1f), 2);
            detail = Text("", new Vector2(dx + dw / 2f, (fy + 1f + py - 1f) / 2f), 0.6f, dw - 1.4f, Color.white, TextAlignmentOptions.Top, height: fy - py, wrap: true);
            ShowDetail();
        }

        private void ShowDetail()
        {
            if (detail == null) return;
            var e = cellOf.Values.FirstOrDefault(x => x.Key == selected);
            if (e == null) { detail.text = ""; return; }
            string gc = "#" + ColorUtility.ToHtmlStringRGB(GroupColor(e.Group, true));
            string ec = "#" + ColorUtility.ToHtmlStringRGB(ElementColor(e.Element));
            var lines = new List<string>();
            if (!e.Found)
            {
                lines.Add("<size=140%>???</size>");
                lines.Add("");
                lines.Add($"<color={gc}>{e.Group.ToUpperInvariant()}</color>");
                lines.Add("");
                lines.Add(e.Kind == "team" ? "NOT FACED YET. MEET THEM IN CLASSIC, CO-OP OR INFINITE." : "NOT FOUND YET. IT SHOWS UP HERE ONCE AN UPGRADE SCREEN OFFERS IT.");
                detail.text = string.Join("\n", lines);
                return;
            }
            lines.Add($"<size=140%><noparse>{e.Title.ToUpperInvariant()}</noparse></size>");
            string tags = $"<color={gc}>{e.Group.ToUpperInvariant()}</color>";
            if (e.Element != "none") tags += $"   <color={ec}>{e.Element.ToUpperInvariant()}</color>";
            if (e.Kind == "card" && e.Style != "game") tags += $"   {e.Style.ToUpperInvariant()}";
            lines.Add(tags);
            lines.Add("");
            if (!string.IsNullOrEmpty(e.Text)) lines.Add($"<noparse>{e.Text}</noparse>");
            foreach (var l in e.Lines) lines.Add($"<color=#FFD95A><noparse>{l}</noparse></color>");
            if (!string.IsNullOrEmpty(e.Flavour)) { lines.Add(""); lines.Add($"<i><color=#BFE6FF><noparse>{e.Flavour}</noparse></color></i>"); }
            lines.Add("");
            lines.Add(e.Kind == "team" ? $"FACED {e.Seen}  ·  BEATEN {e.Beaten}" : $"OFFERED {e.Seen}  ·  PICKED {e.Picked}");
            detail.text = string.Join("\n", lines);
        }

        private void BuildStats(float top, float left, List<CollectionEntry> teams, List<CollectionEntry> perks, List<CollectionEntry> cards)
        {
            var r = Records.Data;
            float y = top - 7.2f;
            float[] cx = { left + pw * 0.2f, left + pw * 0.38f, left + pw * 0.5f, left + pw * 0.62f, left + pw * 0.74f, left + pw * 0.86f };
            string[] head = { "MODE", "WINS", "LOSSES", "WIN %", "STREAK", "BEST STREAK" };
            for (int i = 0; i < head.Length; i++) Text(head[i], new Vector2(cx[i], y), 0.5f, 9f, new Color(1f, 0.85f, 0.35f));
            y -= 1.9f;
            string[] modes = { Records.Classic, Records.Coop, Records.Infinite, Records.Draft, Records.VersusLocal, Records.VersusOnline };
            foreach (var m in modes)
            {
                int i = Array.IndexOf(r.modes, m);
                int w = i >= 0 ? r.wins[i] : 0, l = i >= 0 ? r.losses[i] : 0;
                string[] cells = { m.ToUpperInvariant(), w.ToString(), l.ToString(), w + l > 0 ? $"{Mathf.RoundToInt(100f * w / (w + l))}%" : "-", (i >= 0 ? r.streak[i] : 0).ToString(), (i >= 0 ? r.best[i] : 0).ToString() };
                for (int c = 0; c < cells.Length; c++) Text(cells[c], new Vector2(cx[c], y), 0.55f, 9f, Color.white);
                y -= 1.7f;
            }
            y -= 1f;
            var faced = teams.Where(e => e.Seen > 0).OrderByDescending(e => e.Seen).FirstOrDefault();
            var beaten = teams.Where(e => e.Beaten > 0).OrderByDescending(e => e.Beaten).FirstOrDefault();
            var picked = perks.Concat(cards).Where(e => e.Picked > 0).OrderByDescending(e => e.Picked).FirstOrDefault();
            string Pct(List<CollectionEntry> l) => $"{l.Count(e => e.Found)} / {l.Count}  ({Mathf.RoundToInt(100f * l.Count(e => e.Found) / Mathf.Max(1, l.Count))}%)";
            // two columns of "LABEL  value": records on the left, the collection on the right
            var records = new List<(string, string)>
            {
                ("CLASSIC RUNS WON", r.classicRunsWon.ToString()),
                ("DRAFT CUPS WON", $"{r.draftCupsWon} OF {r.draftsPlayed}"),
                ("HIGHEST INFINITE MATCH WON", r.infiniteBestRound > 0 ? r.infiniteBestRound.ToString() : "-"),
                ("BEST INFINITE RUN", RunState.Save.bestInfiniteWins + " WINS"),
                ("MOST FACED TEAM", faced != null ? $"{faced.Title.ToUpperInvariant()} ({faced.Seen})" : "-"),
                ("MOST BEATEN TEAM", beaten != null ? $"{beaten.Title.ToUpperInvariant()} ({beaten.Beaten})" : "-"),
            };
            var found = new List<(string, string)>
            {
                ("TEAMS FOUND", Pct(teams)),
                ("PERKS FOUND", Pct(perks)),
                ("STAT CARDS FOUND", Pct(cards)),
                ("MOST PICKED CARD", picked != null ? $"{picked.Title.ToUpperInvariant()} ({picked.Picked})" : "-"),
            };
            void Column(List<(string, string)> rows, float mid, float y0)
            {
                foreach (var (k, v) in rows)
                {
                    Text(k, new Vector2(mid - 0.6f - pw * 0.12f, y0), 0.5f, pw * 0.24f, new Color(0.85f, 0.95f, 1f), TextAlignmentOptions.Right);
                    Text($"<noparse>{v}</noparse>", new Vector2(mid + 0.6f + pw * 0.12f, y0), 0.5f, pw * 0.24f, Color.white, TextAlignmentOptions.Left);
                    y0 -= 1.6f;
                }
            }
            Column(records, -pw * 0.24f, y);
            Column(found, pw * 0.24f, y);
        }

        private static Color ElementColor(string id)
        {
            var el = Elements.All.FirstOrDefault(e => e.Id == id);
            return el.Id != null && ColorUtility.TryParseHtmlString("#" + el.Color, out var c) && id != "none" ? c : new Color(0.55f, 0.6f, 0.68f);
        }

        private static Color GroupColor(string g, bool found)
        {
            if (!found) return new Color(0.3f, 0.4f, 0.5f);
            return g == "epic" || g == "boss" ? new Color(1f, 0.78f, 0.29f) : g == "rare" || g == "combo" ? new Color(0.5f, 0.85f, 1f) : g == "game" ? new Color(0.75f, 0.75f, 0.75f) : Color.white;
        }

        // ------------------------------------------------------------ input

        private void Update()
        {
            bool used = NavUpdate();
            // the highlight moving onto a card shows it in the detail pane
            if (focus != lastFocus)
            {
                lastFocus = focus;
                if (focus >= 0 && focus < stops.Count && cellOf.TryGetValue(stops[focus].Box, out var e) && e.Key != selected) { selected = e.Key; ShowDetail(); }
            }
            if (used) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Destroy(gameObject);
        }

        protected override void OnNavBack() => Destroy(gameObject);
        protected override void OnNavTab(int dir) => SetTab((tab + dir + Tabs.Length) % Tabs.Length);

        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }
    }
}

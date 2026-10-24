using System;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Online;
using TMPro;
using UnityEngine;
using Rules = HangtimeOvertime.Generated.Community;

namespace HangtimeOvertime.Patches
{
    // A line at the top of the screen for a few seconds (shown once the scene that follows a run's end has settled).
    internal class Toast : OvertimePanel
    {
        private static string pending;
        private static float askedAt;
        private float until;

        public static void Show(string text) { pending = text; askedAt = Time.unscaledTime; }

        // NetRunner
        public static void Tick()
        {
            if (pending == null || Time.unscaledTime - askedAt < 2.5f || Camera.main == null) return;
            var go = new GameObject("Overtime Toast");
            var t = go.AddComponent<Toast>();
            if (!t.InitKit(320)) { Destroy(go); return; }
            t.Box("Back", new Vector2(0f, t.H / 2f - 2.6f), new Vector2(t.W * 0.7f, 1.8f), new Color(0.02f, 0.12f, 0.22f, 0.88f), 0);
            t.Text("<noparse>" + pending + "</noparse>", new Vector2(0f, t.H / 2f - 2.6f), 0.5f, t.W * 0.68f, new Color(1f, 0.85f, 0.35f));
            t.until = Time.unscaledTime + 6f;
            pending = null;
        }

        private void Update() { if (Time.unscaledTime > until) Destroy(gameObject); }
    }

    // LEADERBOARDS (title bar): CLASSIC SPEEDRUN and INFINITE for this mod version, the top results, your own rank and best,
    // your name, and what happened to your last result.
    internal class LeaderboardMenu : OvertimePanel
    {
        private static LeaderboardMenu open;
        public static bool IsOpen => open != null;
        private static int tab;
        private static readonly string[] Tabs = { "CLASSIC SPEEDRUN", "INFINITE" };
        private static readonly string[] Boards = { Leaderboards.Classic, Leaderboards.Infinite };
        private BoardReply reply;
        private string error, status = "";
        private int page;
        private const int Rows = 11;
        private Field nameField;
        private bool loading;

        public static void Open()
        {
            if (open != null) return;
            var go = new GameObject("Overtime Leaderboards");
            open = go.AddComponent<LeaderboardMenu>();
            if (!open.InitKit(200)) { Destroy(go); open = null; return; }
            open.HoldTitle();
            open.Load();
        }

        private void Load()
        {
            reply = null; error = null; loading = true; page = 0;
            Build();
            if (!Community.Configured) { loading = false; error = "NOT CONNECTED: THE COMMUNITY SERVER ISN'T SET UP YET"; Build(); return; }
            Leaderboards.Flush();
            int asked = tab;
            Leaderboards.Board(Boards[tab], (r, err) => { if (this == null || asked != tab) return; loading = false; reply = r; error = err; Build(); });
        }

        private const float RowH = 46f;

        // the overhaul's layout: ribbon, folder tabs, a ranking table (medals for the top three, your row in orange), your
        // rank and name on the right
        private void Build()
        {
            int keep = focus;
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops();
            navKeys = false;
            Frame("LEADERBOARDS", () => Destroy(gameObject));
            Tabs(60, 92, 300, new[] { "CLASSIC SPEEDRUN", "INFINITE" }, new[] { "clock", "ball" }, tab, SetTab);
            TL("LB / RB", 690, 120, 18, new Color(0.7f, 1f, 0.75f), 90);
            string ver = Community.BoardVersion(Plugin.Version);
            string what = tab == 0 ? "FASTEST CLASSIC CLEAR  ·  REAL TIME, PAUSES DON'T COUNT" : "MOST ROUNDS WON IN ONE SOLO INFINITE RUN";
            TL(what, 90, 178, 22, new Color(0.78f, 0.88f, 1f), 700);

            // the table
            Rect("Table", 70, 200, 1060, 800, new Color(0.03f, 0.11f, 0.2f, 1f), 2);
            float[] cx = { 130, 420, 740, 880, 1000 };
            string[] head = { "RANK", "PLAYER", "RESULT", "DATE", "VERSION" };
            for (int i = 0; i < head.Length; i++) TC(head[i], cx[i], 226, 20, UiArt.Gold, i == 1 ? 300 : 150);
            float y = 268;
            if (loading || error != null || reply == null || reply.ranks == null || reply.ranks.Length == 0)
                TW(loading ? "LOADING..." : error ?? "NO RESULTS YET: BE THE FIRST", 565, 480, 34, UiArt.Ink, 880, 120);
            else
            {
                int n = reply.ranks.Length;
                var medal = new[] { new Color(1f, 0.82f, 0.29f), new Color(0.82f, 0.86f, 0.92f), new Color(0.86f, 0.55f, 0.3f) };
                for (int i = page * Rows; i < Math.Min(n, (page + 1) * Rows); i++)
                {
                    bool me = reply.ids[i] == Identity.Id;
                    int rank = reply.ranks[i];
                    Rect("Row", 84, y - RowH / 2f + 3f, 1046, y + RowH / 2f - 3f, me ? new Color(1f, 0.6f, 0.2f, 1f) : (i % 2 == 0 ? new Color(0.094f, 0.275f, 0.46f, 1f) : new Color(0.07f, 0.2f, 0.35f, 1f)), 3);
                    var col = me ? UiArt.Dark : UiArt.Ink;
                    if (rank <= 3)
                    {
                        Icon("star", P(cx[0] - 22f, y), 1.1f, me ? UiArt.Dark : medal[rank - 1], 6);
                        TC("#" + rank, cx[0] + 14f, y + 1f, 28, me ? UiArt.Dark : medal[rank - 1], 60);
                    }
                    else TC("#" + rank, cx[0], y + 1f, 26, col, 100);
                    TL($"<noparse>{reply.names[i]}</noparse>", 300, y + 1f, 28, col, 320);
                    TC(Leaderboards.Format(Boards[tab], reply.values[i]), cx[2], y + 1f, 28, me ? UiArt.Dark : UiArt.Gold, 180);
                    TC(Date(reply.dates[i]), cx[3], y + 1f, 20, col, 140);
                    TC("v" + reply.mods[i], cx[4], y + 1f, 20, col, 110);
                    y += RowH;
                }
            }
            int pages = reply?.ranks == null ? 1 : Math.Max(1, (reply.ranks.Length + Rows - 1) / Rows);
            if (pages > 1)
            {
                Btn("<", 460, 812, 500, 840, () => { page = (page - 1 + pages) % pages; Build(); }, 24);
                TC($"{page + 1} / {pages}", 565, 826, 20, new Color(0.78f, 0.88f, 1f), 100);
                Btn(">", 630, 812, 670, 840, () => { page = (page + 1) % pages; Build(); }, 24);
            }

            // right: you
            Rect("You", 1090, 200, 1530, 800, new Color(0.03f, 0.11f, 0.2f, 1f), 2);
            TC("YOU", 1310, 238, 34, UiArt.Gold, 300);
            if (reply != null && reply.meRank > 0)
            {
                TC("#" + reply.meRank, 1310, 312, 90, UiArt.Ink, 380);
                TC(Leaderboards.Format(Boards[tab], reply.meValue), 1310, 392, 40, UiArt.Gold, 380);
                TC(Date(reply.meDate), 1310, 432, 20, new Color(0.78f, 0.88f, 1f), 300);
            }
            else TW(reply == null ? "" : "NO RESULT ON THIS BOARD YET", 1310, 340, 30, UiArt.Ink, 380, 120);
            TC($"BOARD v{ver}.x" + (reply != null ? $"  ·  {reply.total} {(reply.total == 1 ? "PLAYER" : "PLAYERS")}" : ""), 1310, 470, 19, new Color(0.78f, 0.88f, 1f), 400);
            TL("YOUR NAME", 1120, 520, 22, UiArt.Gold, 200);
            nameField = TextField(P(1310, 562), S(380, 50), "TYPE A NAME", Rules.NameMax, v => SaveName(v), Identity.Name);
            IconBtn("check", "SAVE NAME", 1170, 600, 1450, 646, () => SaveName(nameField.Value), 24);
            int queued = Leaderboards.PendingCount;
            string st = status.Length > 0 ? status : Leaderboards.LastStatus.Length > 0 ? "LAST RESULT: " + Leaderboards.LastStatus : "";
            if (queued > 0) st += (st.Length > 0 ? "  ·  " : "") + $"{queued} WAITING TO SUBMIT";
            if (!Plugin.CommunitySubmit.Value) st += (st.Length > 0 ? "  ·  " : "") + "SUBMITTING IS OFF (SETTINGS)";
            TW("<noparse>" + st + "</noparse>", 1310, 712, 19, new Color(0.78f, 0.88f, 1f), 400, 110);
            IconBtn("refresh", "REFRESH", 1170, 846, 1400, 892, Load);
            PadHint("A: CHOOSE   B: BACK   LB / RB: BOARDS", P(620, 870), 600 / 25f * KX);
            focus = keep >= 0 && keep < stops.Count ? keep : 0;
        }

        private static string Date(double ms) => ms <= 0 ? "" : DateTimeOffset.FromUnixTimeMilliseconds((long)ms).ToLocalTime().ToString("yyyy-MM-dd");

        private void SaveName(string v)
        {
            var why = Identity.SetName(v);
            status = why ?? "NAME SAVED";
            Leaderboards.Flush();
            Build();
        }

        private void SetTab(int t) { if (t == tab) return; tab = t; focus = -1; Load(); }

        private void Update()
        {
            if (FieldKeys() || Typing) return;
            if (NavUpdate()) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Destroy(gameObject);
        }

        protected override void OnNavBack() => Destroy(gameObject);
        protected override void OnNavTab(int dir) => SetTab((tab + dir + Tabs.Length) % Tabs.Length);
        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }
    }
}

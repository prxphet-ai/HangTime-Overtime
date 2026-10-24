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
        private const int Rows = 12;
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

        private void Build()
        {
            int keep = focus;
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops();
            navKeys = false;
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            float pw = W * 0.86f, ph = H * 0.92f, top = ph / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            Text("LEADERBOARDS", new Vector2(0f, top - 1.7f), 1.1f, pw - 2f, Color.white);
            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                var b = Button(Tabs[i], new Vector2((i - 0.5f) * pw * 0.3f, top - 3.8f), new Vector2(pw * 0.28f, 1.9f), () => SetTab(idx), 0.5f);
                b.color = i == tab ? new Color(0.96f, 0.55f, 0.12f, 1f) : new Color(0.16f, 0.6f, 0.86f, 1f);
            }
            string ver = Community.BoardVersion(Plugin.Version);
            string what = tab == 0 ? "FASTEST CLASSIC CLEAR (REAL TIME, PAUSES DON'T COUNT)" : "MOST ROUNDS WON IN ONE SOLO INFINITE RUN";
            Text($"{what}  ·  BOARD v{ver}.x" + (reply != null ? $"  ·  {reply.total} {(reply.total == 1 ? "PLAYER" : "PLAYERS")}" : ""), new Vector2(0f, top - 5.5f), 0.42f, pw - 2f, new Color(0.85f, 0.95f, 1f));

            // the table
            float y = top - 7.2f, rowH = 1.45f;
            float[] cx = { -pw * 0.4f, -pw * 0.2f, pw * 0.08f, pw * 0.26f, pw * 0.4f };
            string[] head = { "RANK", "PLAYER", "RESULT", "DATE", "VERSION" };
            for (int i = 0; i < head.Length; i++) Text(head[i], new Vector2(cx[i], y), 0.45f, pw * 0.2f, new Color(1f, 0.85f, 0.35f));
            y -= rowH;
            Box("Table", new Vector2(0f, y - rowH * (Rows - 1) / 2f), new Vector2(pw - 2f, rowH * Rows + 0.3f), new Color(0.02f, 0.2f, 0.34f, 1f), 2);
            if (loading || error != null || reply == null || reply.ranks == null || reply.ranks.Length == 0)
                Text(loading ? "LOADING..." : error ?? "NO RESULTS YET: BE THE FIRST", new Vector2(0f, y - rowH * 5f), 0.55f, pw - 4f, Color.white);
            else
            {
                int n = reply.ranks.Length;
                for (int i = page * Rows; i < Math.Min(n, (page + 1) * Rows); i++)
                {
                    bool me = reply.ids[i] == Identity.Id;
                    if (me) Box("Me", new Vector2(0f, y), new Vector2(pw - 2.4f, rowH - 0.1f), new Color(0.96f, 0.55f, 0.12f, 0.45f), 3);
                    var col = me ? Color.white : new Color(0.9f, 0.96f, 1f);
                    Text("#" + reply.ranks[i], new Vector2(cx[0], y), 0.5f, pw * 0.15f, col);
                    Text($"<noparse>{reply.names[i]}</noparse>", new Vector2(cx[1], y), 0.5f, pw * 0.3f, col);
                    Text(Leaderboards.Format(Boards[tab], reply.values[i]), new Vector2(cx[2], y), 0.5f, pw * 0.2f, col);
                    Text(Date(reply.dates[i]), new Vector2(cx[3], y), 0.45f, pw * 0.18f, col);
                    Text("v" + reply.mods[i], new Vector2(cx[4], y), 0.45f, pw * 0.12f, col);
                    y -= rowH;
                }
            }
            float ty = top - 7.2f - rowH * (Rows + 1) - 0.4f;
            int pages = reply?.ranks == null ? 1 : Math.Max(1, (reply.ranks.Length + Rows - 1) / Rows);
            if (pages > 1)
            {
                Button("<", new Vector2(-pw * 0.43f, ty), new Vector2(1.8f, 1.6f), () => { page = (page - 1 + pages) % pages; Build(); }, 0.6f);
                Button(">", new Vector2(-pw * 0.36f, ty), new Vector2(1.8f, 1.6f), () => { page = (page + 1) % pages; Build(); }, 0.6f);
            }
            string mine = reply == null ? "" : reply.meRank > 0 ? $"YOU: #{reply.meRank} · {Leaderboards.Format(Boards[tab], reply.meValue)} · {Date(reply.meDate)}" : "YOU: NO RESULT ON THIS BOARD YET";
            Text(mine, new Vector2(0f, ty), 0.55f, pw * 0.6f, new Color(1f, 0.85f, 0.35f));

            // name, status, buttons
            float ny = -ph / 2f + 5.2f;
            Text("YOUR NAME", new Vector2(-pw * 0.36f, ny), 0.45f, pw * 0.2f, Color.white);
            nameField = TextField(new Vector2(-pw * 0.12f, ny), new Vector2(pw * 0.26f, 1.7f), "TYPE A NAME", Rules.NameMax, v => SaveName(v), Identity.Name);
            Button("SAVE NAME", new Vector2(pw * 0.1f, ny), new Vector2(pw * 0.14f, 1.7f), () => SaveName(nameField.Value), 0.45f);
            int queued = Leaderboards.PendingCount;
            string st = status.Length > 0 ? status : Leaderboards.LastStatus.Length > 0 ? "LAST RESULT: " + Leaderboards.LastStatus : "";
            if (queued > 0) st += (st.Length > 0 ? "  ·  " : "") + $"{queued} WAITING TO SUBMIT";
            if (!Plugin.CommunitySubmit.Value) st += (st.Length > 0 ? "  ·  " : "") + "SUBMITTING IS OFF (SETTINGS)";
            Text("<noparse>" + st + "</noparse>", new Vector2(0f, ny - 1.7f), 0.4f, pw - 2f, new Color(0.85f, 0.95f, 1f));
            float by = -ph / 2f + 1.6f;
            Button("REFRESH", new Vector2(-pw * 0.15f, by), new Vector2(pw * 0.18f, 2.1f), Load, 0.55f);
            Button("BACK", new Vector2(pw * 0.15f, by), new Vector2(pw * 0.18f, 2.1f), () => Destroy(gameObject), 0.6f);
            PadHint("A: CHOOSE · B: BACK · LB / RB: BOARDS", new Vector2(pw * 0.36f, by), pw * 0.25f);
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

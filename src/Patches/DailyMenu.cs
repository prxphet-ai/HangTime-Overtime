using System;
using System.Globalization;
using System.Linq;
using HangtimeOvertime.Core;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // DAILY RUN (title bar): today's seeded Infinite run (RUN tab) and today's seeded 8-match cup (CUP tab, its whole
    // bracket shown). START (or PRACTICE once today's has been played), CONTINUE a saved one, today's result and the
    // history of past days.
    internal class DailyMenu : OvertimePanel
    {
        private static DailyMenu open;
        public static bool IsOpen => open != null;
        private TextMeshPro clock;
        private string shownDay;
        private static bool cup;                           // the CUP tab (else RUN)

        public static void Open()
        {
            if (open != null) return;
            var go = new GameObject("Overtime Daily");
            open = go.AddComponent<DailyMenu>();
            if (!open.InitKit(200)) { Destroy(go); open = null; return; }
            open.HoldTitle();
            open.Build();
        }

        private void Build()
        {
            int keep = focus;
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops();
            navKeys = true;
            string today = Daily.Today;
            shownDay = today;
            string key = Daily.Key(today, cup);
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            float pw = W * 0.66f, ph = H * 0.9f, top = ph / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            Text("DAILY", new Vector2(-pw / 2f + 5f, top - 1.8f), 1.1f, 9f, Color.white);
            var date = DateTime.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            Text(date.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant() + "  (UTC)", new Vector2(pw * 0.12f, top - 1.5f), 0.55f, pw * 0.7f, new Color(1f, 0.85f, 0.35f));
            clock = Text("", new Vector2(pw * 0.12f, top - 2.6f), 0.45f, pw * 0.7f, new Color(0.85f, 0.95f, 1f));
            string[] tabs = { "RUN (INFINITE)", "CUP (BRACKET)" };
            for (int i = 0; i < 2; i++)
            {
                bool isCup = i == 1;
                var b = Button(tabs[i], new Vector2((i - 0.5f) * pw * 0.32f, top - 4.4f), new Vector2(pw * 0.3f, 1.9f), () => SetTab(isCup), 0.5f);
                b.color = isCup == cup ? new Color(0.96f, 0.55f, 0.12f, 1f) : new Color(0.16f, 0.6f, 0.86f, 1f);
            }
            Text(cup ? "AN 8-MATCH BRACKET WITH THE SAME TEAMS AND CARD OFFERS FOR EVERYONE TODAY, AT CLASSIC DIFFICULTY. YOUR FIRST CUP OF THE DAY COUNTS; MORE ARE PRACTICE."
                     : "AN INFINITE RUN WITH THE SAME OPPONENTS AND CARD OFFERS FOR EVERYONE TODAY. YOUR FIRST RUN OF THE DAY COUNTS; MORE RUNS ARE PRACTICE (THEY ONLY COUNT FOR TODAY'S BEST).",
                 new Vector2(0f, top - 6.4f), 0.42f, pw - 3f, new Color(0.85f, 0.95f, 1f), height: 2.4f, wrap: true);
            float y0 = top - 8.2f;
            if (cup)
            {
                // today's bracket is known in advance
                var names = Enumerable.Range(1, 8).Select(g => Engine.TeamRoster.CupName(g, today).ToUpperInvariant());
                Text("<noparse>" + string.Join("  >  ", names) + "</noparse>", new Vector2(0f, y0), 0.38f, pw - 2.5f, Color.white, height: 2f, wrap: true);
                y0 -= 1.4f;
            }

            // today
            var snap = cup ? RunSaves.DailyCup : RunSaves.DailyRun;
            bool saved = snap.has && !string.IsNullOrEmpty(snap.daily);
            int runs = Daily.Runs(key), first = Daily.First(key), best = Daily.Best(key);
            string word = cup ? "CUP" : "RUN";
            string state = runs == 0 ? "TODAY: NOT PLAYED YET"
                : first < 0 && saved && snap.daily == today && snap.dailyCounts ? $"TODAY: YOUR {word} THAT COUNTS IS SAVED AT {RunSaves.Where(snap).ToUpperInvariant()}"
                : first < 0 ? $"TODAY: YOUR FIRST {word} IS STILL GOING"
                : $"TODAY: FIRST {word} {Result(first)}  ·  BEST {Result(best)}  ·  {runs} {(runs == 1 ? "TRY" : "TRIES")}";
            float ty = y0 - 1.6f;
            Box("Today", new Vector2(0f, ty), new Vector2(pw - 2f, 2f), new Color(0.02f, 0.2f, 0.34f, 1f), 2);
            Text(state, new Vector2(0f, ty), 0.55f, pw - 3f, Color.white);

            float by = ty - 2.7f;
            bool counts = runs == 0;
            string startLabel = counts ? $"START TODAY'S {word}" : $"PRACTICE {word}";
            if (saved)
            {
                string label = snap.daily == today ? $"CONTINUE  ({RunSaves.Where(snap).ToUpperInvariant()})" : $"CONTINUE {snap.daily}";
                Button(label, new Vector2(-pw * 0.22f, by), new Vector2(pw * 0.4f, 2.3f), () => Continue(snap), 0.55f);
                Button(startLabel, new Vector2(pw * 0.22f, by), new Vector2(pw * 0.4f, 2.3f), () => StartRun(snap), 0.55f);
                Text(snap.dailyCounts ? $"STARTING ANOTHER {word} ENDS THE SAVED ONE: IT COUNTS WITH THE WINS IT HAS" : $"STARTING ANOTHER {word} DROPS THE SAVED PRACTICE {word}",
                     new Vector2(0f, by - 1.7f), 0.38f, pw - 2f, new Color(1f, 0.85f, 0.35f));
            }
            else Button(startLabel, new Vector2(0f, by), new Vector2(pw * 0.45f, 2.3f), () => StartRun(null), 0.6f);

            // history
            float y = by - 3.5f;
            float[] cx = { -pw * 0.3f, -pw * 0.05f, pw * 0.15f, pw * 0.32f };
            string[] head = { "DAY", "FIRST " + word, "BEST", "TRIES" };
            for (int i = 0; i < head.Length; i++) Text(head[i], new Vector2(cx[i], y), 0.45f, pw * 0.25f, new Color(1f, 0.85f, 0.35f));
            y -= 1.4f;
            var hist = Daily.History(cup).Take(cup ? 6 : 7).ToList();
            if (hist.Count == 0) Text($"NO DAILY {word}S YET", new Vector2(0f, y - 1f), 0.5f, pw - 2f, new Color(0.85f, 0.95f, 1f));
            foreach (var (d, f, b, r) in hist)
            {
                var col = d == today ? Color.white : new Color(0.85f, 0.95f, 1f);
                Text(d == today ? d + " (TODAY)" : d, new Vector2(cx[0], y), 0.48f, pw * 0.3f, col);
                Text(f < 0 ? "..." : Result(f), new Vector2(cx[1], y), 0.48f, pw * 0.2f, col);
                Text(Result(b), new Vector2(cx[2], y), 0.48f, pw * 0.2f, col);
                Text(r.ToString(), new Vector2(cx[3], y), 0.48f, pw * 0.2f, col);
                y -= 1.25f;
            }
            var all = Daily.History(cup).Where(h => h.first >= 0).ToList();
            if (all.Count > 0)
            {
                var top1 = all.OrderByDescending(h => h.first).First();
                string extra = cup ? $"CUPS WON: {all.Count(h => h.first >= 8)}  ·  " : "";
                Text($"BEST: {Result(top1.first)} ({top1.date})  ·  {extra}DAYS PLAYED: {Daily.History(cup).Count()}", new Vector2(0f, -ph / 2f + 4.1f), 0.45f, pw - 2f, new Color(1f, 0.85f, 0.35f));
            }
            Button("BACK", new Vector2(0f, -ph / 2f + 1.7f), new Vector2(pw * 0.3f, 2.2f), () => Destroy(gameObject), 0.6f);
            PadHint("A: CHOOSE   ·   B: BACK   ·   LB / RB: RUN / CUP", new Vector2(0f, -ph / 2f + 3f), pw - 2f);
            focus = keep >= 0 && keep < stops.Count ? keep : 2;      // the first button under the tabs
        }

        // a result: run = wins, cup = matches won of 8
        private static string Result(int w) => cup ? (w >= 8 ? "CHAMPION" : $"{w} / 8 WON") : $"{w} {(w == 1 ? "WIN" : "WINS")}";

        private void SetTab(bool c) { if (cup == c) return; cup = c; focus = -1; Build(); }
        protected override void OnNavTab(int dir) => SetTab(!cup);

        // the title button that starts this kind of run: INFINITE for the run, the 1-player CLASSIC for the cup
        private static TitleButton StartButton() =>
            FindObjectsByType<TitleButton>(FindObjectsSortMode.None)
                .FirstOrDefault(b => cup ? !ContinueMenu.IsInfinite(b) && ContinueMenu.Players(b) == 1 : ContinueMenu.IsInfinite(b));

        private void StartRun(RunSnapshot saved)
        {
            var tb = StartButton();
            if (tb == null) return;
            if (saved != null && saved.has)
            {
                if (saved.dailyCounts) Daily.Finish(Daily.Key(saved.daily, cup), true, cup ? Mathf.Max(0, saved.gameNumber - 1) : saved.infiniteWins);
                RunSaves.ClearDaily(cup);
            }
            Daily.Pending = Daily.Today;
            Daily.PendingCup = cup;
            ContinueMenu.Bypass = true;
            Destroy(gameObject);
            tb.Click();
        }

        private void Continue(RunSnapshot snap)
        {
            var tb = StartButton();
            if (tb == null) return;
            Destroy(gameObject);
            ContinueMenu.Resume(tb, snap);
        }

        private void Update()
        {
            if (Daily.Today != shownDay) { Build(); return; }        // midnight UTC while the menu is open
            var left = Daily.UntilNext;
            if (clock != null) clock.text = $"NEXT DAILY IN {(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}";
            if (NavUpdate()) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Destroy(gameObject);
        }

        protected override void OnNavBack() => Destroy(gameObject);
        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }
    }
}

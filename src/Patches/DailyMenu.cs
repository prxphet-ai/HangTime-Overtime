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
            Frame("DAILY", () => Destroy(gameObject));
            Tabs(60, 92, 300, new[] { "RUN (INFINITE)", "CUP (BRACKET)" }, new[] { "ball", "trophy" }, cup ? 1 : 0, i => SetTab(i == 1));
            TL("LB / RB", 690, 120, 18, new Color(0.7f, 1f, 0.75f), 90);
            var date = DateTime.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            TR(date.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant() + "  (UTC)", 1530, 102, 26, UiArt.Gold, 600);
            clock = TR("", 1530, 128, 20, new Color(0.78f, 0.88f, 1f), 600);

            // left: today
            Rect("Today", 70, 170, 760, 800, new Color(0.03f, 0.11f, 0.2f, 1f), 2);
            TC("TODAY", 415, 206, 40, UiArt.Gold, 600);
            TW(cup ? "AN 8-MATCH BRACKET WITH THE SAME TEAMS AND CARD OFFERS FOR EVERYONE TODAY, AT CLASSIC DIFFICULTY. YOUR FIRST CUP OF THE DAY COUNTS; MORE ARE PRACTICE."
                   : "AN INFINITE RUN WITH THE SAME OPPONENTS AND CARD OFFERS FOR EVERYONE TODAY. YOUR FIRST RUN OF THE DAY COUNTS; MORE RUNS ARE PRACTICE (THEY ONLY COUNT FOR TODAY'S BEST).",
               415, 266, 21, new Color(0.78f, 0.88f, 1f), 620, 80);
            float y = 320f;
            if (cup)
            {
                // today's bracket is known in advance: the eight teams in order
                for (int g = 1; g <= 8; g++)
                {
                    float x0 = 96f + (g - 1) % 4 * 160f, y0 = y + (g - 1) / 4 * 92f;
                    Rect("Match", x0, y0, x0 + 150f, y0 + 82f, new Color(0.094f, 0.275f, 0.46f, 1f), 3);
                    TC(g == 8 ? "FINAL" : $"MATCH {g}", x0 + 75f, y0 + 20f, 15, g == 3 || g == 6 || g == 8 ? UiArt.Gold : new Color(0.78f, 0.88f, 1f), 140);
                    TW("<noparse>" + Engine.TeamRoster.CupName(g, today).ToUpperInvariant() + "</noparse>", x0 + 75f, y0 + 54f, 19, UiArt.Ink, 140, 44);
                }
                y += 196f;
            }

            var snap = cup ? RunSaves.DailyCup : RunSaves.DailyRun;
            bool saved = snap.has && !string.IsNullOrEmpty(snap.daily);
            int runs = Daily.Runs(key), first = Daily.First(key), best = Daily.Best(key);
            string word = cup ? "CUP" : "RUN";
            string state = runs == 0 ? "NOT PLAYED YET"
                : first < 0 && saved && snap.daily == today && snap.dailyCounts ? $"YOUR {word} THAT COUNTS IS SAVED AT {RunSaves.Where(snap).ToUpperInvariant()}"
                : first < 0 ? $"YOUR FIRST {word} IS STILL GOING"
                : $"FIRST {word} {Result(first)}  ·  BEST {Result(best)}  ·  {runs} {(runs == 1 ? "TRY" : "TRIES")}";
            Rect("State", 96, y, 734, y + 70, new Color(0.094f, 0.275f, 0.46f, 1f), 3, true);
            TW(state, 415, y + 35, 26, UiArt.Ink, 610, 64);
            y += 96f;
            bool counts = runs == 0;
            string startLabel = counts ? $"START TODAY'S {word}" : $"PRACTICE {word}";
            if (saved)
            {
                string label = snap.daily == today ? $"CONTINUE ({RunSaves.Where(snap).ToUpperInvariant()})" : $"CONTINUE {snap.daily}";
                GoldSign(label, 110, y, 720, y + 74, () => Continue(snap), 30);
                IconBtn("refresh", startLabel, 160, y + 90, 670, y + 140, () => StartRun(snap), 24);
                TW(snap.dailyCounts ? $"STARTING ANOTHER {word} ENDS THE SAVED ONE: IT COUNTS WITH THE WINS IT HAS" : $"STARTING ANOTHER {word} DROPS THE SAVED PRACTICE {word}",
                   415, y + 168, 18, UiArt.Gold, 620, 40);
            }
            else GoldSign(startLabel, 110, y, 720, y + 80, () => StartRun(null), 36);

            // right: past days
            Rect("History", 790, 170, 1530, 800, new Color(0.03f, 0.11f, 0.2f, 1f), 2);
            TC("PAST DAYS", 1160, 206, 34, UiArt.Gold, 600);
            float[] cx = { 920, 1130, 1300, 1450 };
            string[] head = { "DAY", "FIRST " + word, "BEST", "TRIES" };
            for (int i = 0; i < head.Length; i++) TC(head[i], cx[i], 250, 20, UiArt.Gold, 200);
            float hy = 292f;
            var hist = Daily.History(cup).Take(9).ToList();
            if (hist.Count == 0) TC($"NO DAILY {word}S YET", 1160, 420, 30, UiArt.Ink, 600);
            int row = 0;
            foreach (var (d, f, bb, r) in hist)
            {
                bool now = d == today;
                Rect("Row", 806, hy - 21, 1514, hy + 21, now ? new Color(1f, 0.6f, 0.2f, 1f) : row % 2 == 0 ? new Color(0.094f, 0.275f, 0.46f, 1f) : new Color(0.07f, 0.2f, 0.35f, 1f), 3);
                var col = now ? UiArt.Dark : UiArt.Ink;
                TC(now ? d + " (TODAY)" : d, cx[0], hy + 1, 22, col, 240);
                TC(f < 0 ? "..." : Result(f), cx[1], hy + 1, 22, now ? UiArt.Dark : UiArt.Gold, 200);
                TC(Result(bb), cx[2], hy + 1, 22, col, 180);
                TC(r.ToString(), cx[3], hy + 1, 22, col, 100);
                hy += 48f; row++;
            }
            var all = Daily.History(cup).Where(h => h.first >= 0).ToList();
            if (all.Count > 0)
            {
                var top1 = all.OrderByDescending(h => h.first).First();
                string extra = cup ? $"CUPS WON: {all.Count(h => h.first >= 8)}  ·  " : "";
                TC($"BEST: {Result(top1.first)} ({top1.date})  ·  {extra}DAYS PLAYED: {Daily.History(cup).Count()}", 1160, 770, 20, UiArt.Gold, 700);
            }
            PadHint("A: CHOOSE   B: BACK   LB / RB: RUN / CUP", P(700, 870), 600 / 25f * KX);
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

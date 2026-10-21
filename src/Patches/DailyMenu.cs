using System;
using System.Globalization;
using System.Linq;
using HangtimeOvertime.Core;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // DAILY (title menu): today's seeded Infinite run. START (or PRACTICE RUN once today's has been played), CONTINUE a
    // saved daily run, today's result and the history of past days.
    internal class DailyMenu : OvertimePanel
    {
        private static DailyMenu open;
        public static bool IsOpen => open != null;
        private TextMeshPro clock;
        private string shownDay;

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
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops();
            navKeys = true;
            string today = Daily.Today;
            shownDay = today;
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            float pw = W * 0.62f, ph = H * 0.88f, top = ph / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            Text("DAILY RUN", new Vector2(0f, top - 1.8f), 1.1f, pw - 2f, Color.white);
            var date = DateTime.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            Text(date.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant() + "  (UTC)", new Vector2(0f, top - 3.5f), 0.55f, pw - 2f, new Color(1f, 0.85f, 0.35f));
            clock = Text("", new Vector2(0f, top - 4.7f), 0.45f, pw - 2f, new Color(0.85f, 0.95f, 1f));
            Text("AN INFINITE RUN WITH THE SAME OPPONENTS AND CARD OFFERS FOR EVERYONE TODAY. YOUR FIRST RUN OF THE DAY COUNTS; " +
                 "MORE RUNS ARE PRACTICE (THEY ONLY COUNT FOR TODAY'S BEST).", new Vector2(0f, top - 6.6f), 0.42f, pw - 3f, new Color(0.85f, 0.95f, 1f), height: 2.4f, wrap: true);

            // today
            var snap = RunSaves.DailyRun;
            bool saved = snap.has && !string.IsNullOrEmpty(snap.daily);
            int runs = Daily.Runs(today), first = Daily.First(today), best = Daily.Best(today);
            string state = runs == 0 ? "TODAY: NOT PLAYED YET"
                : first < 0 && saved && snap.daily == today && snap.dailyCounts ? $"TODAY: YOUR RUN THAT COUNTS IS SAVED AT {RunSaves.Where(snap).ToUpperInvariant()}"
                : first < 0 ? "TODAY: YOUR FIRST RUN IS STILL GOING"
                : $"TODAY: FIRST RUN {first} {(first == 1 ? "WIN" : "WINS")}  ·  BEST {best}  ·  {runs} {(runs == 1 ? "RUN" : "RUNS")}";
            Box("Today", new Vector2(0f, top - 9.3f), new Vector2(pw - 2f, 2f), new Color(0.02f, 0.2f, 0.34f, 1f), 2);
            Text(state, new Vector2(0f, top - 9.3f), 0.55f, pw - 3f, Color.white);

            float by = top - 12f;
            bool counts = runs == 0;
            if (saved)
            {
                string label = snap.daily == today ? $"CONTINUE  ({RunSaves.Where(snap).ToUpperInvariant()})" : $"CONTINUE {snap.daily}";
                Button(label, new Vector2(-pw * 0.22f, by), new Vector2(pw * 0.4f, 2.3f), () => Continue(snap), 0.55f);
                Button(counts ? "START TODAY'S RUN" : "PRACTICE RUN", new Vector2(pw * 0.22f, by), new Vector2(pw * 0.4f, 2.3f), () => StartRun(snap), 0.55f);
            }
            else Button(counts ? "START TODAY'S RUN" : "PRACTICE RUN", new Vector2(0f, by), new Vector2(pw * 0.45f, 2.3f), () => StartRun(null), 0.6f);
            if (saved) Text(snap.dailyCounts ? "STARTING ANOTHER RUN ENDS THE SAVED ONE: IT COUNTS WITH THE WINS IT HAS" : "STARTING ANOTHER RUN DROPS THE SAVED PRACTICE RUN",
                            new Vector2(0f, by - 1.7f), 0.38f, pw - 2f, new Color(1f, 0.85f, 0.35f));

            // history
            float y = by - 3.6f;
            float[] cx = { -pw * 0.3f, -pw * 0.05f, pw * 0.15f, pw * 0.32f };
            string[] head = { "DAY", "FIRST RUN", "BEST", "RUNS" };
            for (int i = 0; i < head.Length; i++) Text(head[i], new Vector2(cx[i], y), 0.45f, pw * 0.25f, new Color(1f, 0.85f, 0.35f));
            y -= 1.5f;
            var hist = Daily.History().Take(8).ToList();
            if (hist.Count == 0) Text("NO DAILY RUNS YET", new Vector2(0f, y - 1f), 0.5f, pw - 2f, new Color(0.85f, 0.95f, 1f));
            foreach (var (d, f, b, r) in hist)
            {
                var col = d == today ? Color.white : new Color(0.85f, 0.95f, 1f);
                Text(d == today ? d + " (TODAY)" : d, new Vector2(cx[0], y), 0.48f, pw * 0.3f, col);
                Text(f < 0 ? "..." : f.ToString(), new Vector2(cx[1], y), 0.48f, pw * 0.2f, col);
                Text(b.ToString(), new Vector2(cx[2], y), 0.48f, pw * 0.2f, col);
                Text(r.ToString(), new Vector2(cx[3], y), 0.48f, pw * 0.2f, col);
                y -= 1.3f;
            }
            var all = Daily.History().Where(h => h.first >= 0).ToList();
            if (all.Count > 0)
            {
                var top1 = all.OrderByDescending(h => h.first).First();
                Text($"BEST DAILY: {top1.first} WINS ({top1.date})  ·  DAYS PLAYED: {Daily.History().Count()}", new Vector2(0f, -ph / 2f + 4f), 0.45f, pw - 2f, new Color(1f, 0.85f, 0.35f));
            }
            Button("BACK", new Vector2(0f, -ph / 2f + 1.7f), new Vector2(pw * 0.3f, 2.2f), () => Destroy(gameObject), 0.6f);
            PadHint("A: CHOOSE   ·   B: BACK", new Vector2(0f, -ph / 2f + 3f), pw - 2f);
            focus = 0;
        }

        private static TitleButton InfiniteButton() =>
            FindObjectsByType<TitleButton>(FindObjectsSortMode.None).FirstOrDefault(ContinueMenu.IsInfinite);

        private void StartRun(RunSnapshot saved)
        {
            var tb = InfiniteButton();
            if (tb == null) return;
            if (saved != null && saved.has)
            {
                if (saved.dailyCounts) Daily.Finish(saved.daily, true, saved.infiniteWins);
                RunSaves.ClearDaily();
            }
            Daily.Pending = Daily.Today;
            ContinueMenu.Bypass = true;
            Destroy(gameObject);
            tb.Click();
        }

        private void Continue(RunSnapshot snap)
        {
            var tb = InfiniteButton();
            if (tb == null) return;
            Destroy(gameObject);
            ContinueMenu.Resume(tb, snap);
        }

        private void Update()
        {
            if (Daily.Today != shownDay) { Build(); return; }        // midnight UTC while the menu is open
            var left = Daily.UntilNext;
            if (clock != null) clock.text = $"NEXT DAILY RUN IN {(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}";
            if (NavUpdate()) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Destroy(gameObject);
        }

        protected override void OnNavBack() => Destroy(gameObject);
        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }
    }
}

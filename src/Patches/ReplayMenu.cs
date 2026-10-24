using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Online;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // REPLAYS (title bar): the recent replays (the last 10 matches / Versus rounds) and the saved ones; WATCH plays one,
    // SAVE keeps a recent one for good, DELETE removes a saved one.
    internal class ReplayMenu : OvertimePanel
    {
        private static ReplayMenu open;
        public static bool IsOpen => open != null;
        private List<Replays.Entry> list;
        private int page;
        private const int Rows = 6;
        private string note = "";

        public static void Open()
        {
            if (open != null) return;
            var go = new GameObject("Overtime Replays");
            open = go.AddComponent<ReplayMenu>();
            if (!open.InitKit(200)) { Destroy(go); open = null; return; }
            open.HoldTitle();
            open.list = Replays.List();
            open.Build();
        }

        private void Build()
        {
            int keep = focus;
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops();
            navKeys = true;
            Frame("REPLAYS", () => Destroy(gameObject));
            TC(Plugin.RecordReplays.Value ? $"THE LAST {Replays.KeepRecent} MATCHES AND VERSUS ROUNDS ARE KEPT; SAVE ONE TO KEEP IT FOR GOOD" : "RECORDING IS OFF (SETTINGS > RECORD REPLAYS)",
               800, 172, 22, new Color(0.78f, 0.88f, 1f), 1400);
            int pages = Mathf.Max(1, (list.Count + Rows - 1) / Rows);
            page = Mathf.Clamp(page, 0, pages - 1);
            float y = 200f;
            if (list.Count == 0)
            {
                Icon("film", P(800, 420), 4f, new Color(1f, 1f, 1f, 0.25f), 3);
                TC("NO REPLAYS YET: PLAY A MATCH", 800, 520, 40, UiArt.Ink, 1000);
            }
            foreach (var e in list.Skip(page * Rows).Take(Rows))
            {
                var h = e.Header;
                var entry = e;
                Rect("Row", 90, y, 1510, y + 84, e.Saved ? new Color(0.16f, 0.36f, 0.55f, 1f) : new Color(0.094f, 0.275f, 0.46f, 1f), 3, true);
                Icon("film", P(130, y + 42), 1.3f, e.Saved ? UiArt.Gold : UiArt.Ink, 7);
                TL($"<noparse>{h.Title}</noparse>", 170, y + 28, 28, UiArt.Ink, 900);
                TL($"{(e.Saved ? "<color=#FFD24A>SAVED</color>  ·  " : "")}{h.Date}  ·  {(int)(h.Seconds / 60)}:{(int)(h.Seconds % 60):00}  ·  <noparse>{h.Left} {h.ScoreL} - {h.ScoreR} {h.Right}</noparse>",
                   170, y + 60, 20, new Color(0.78f, 0.88f, 1f), 900);
                GoldSign("WATCH", 1110, y + 14, 1300, y + 70, () => Watch(entry), 28, "play");
                if (e.Saved) IconBtn("bin", "DELETE", 1316, y + 18, 1490, y + 66, () => { Replays.Delete(entry); list = Replays.List(); note = "DELETED"; Build(); }, 20);
                else IconBtn("check", "SAVE", 1316, y + 18, 1490, y + 66, () => { Replays.Keep(entry); list = Replays.List(); note = "SAVED"; Build(); }, 20);
                y += 94f;
            }
            if (pages > 1)
            {
                Btn("<", 640, 846, 690, 892, () => { page = (page - 1 + pages) % pages; Build(); }, 26);
                TC($"PAGE {page + 1} / {pages}", 800, 869, 22, UiArt.Ink, 200);
                Btn(">", 910, 846, 960, 892, () => { page = (page + 1) % pages; Build(); }, 26);
            }
            TL(note, 80, 869, 22, UiArt.Gold, 500);
            PadHint("A: CHOOSE   B: BACK   LB / RB: PAGES", P(1180, 830), 520 / 25f * KX);
            focus = keep >= 0 && keep < stops.Count ? keep : 0;
        }

        private void Watch(Replays.Entry e)
        {
            Destroy(gameObject);
            Replays.Watch(e);
        }

        private void Update()
        {
            if (NavUpdate()) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Destroy(gameObject);
        }

        protected override void OnNavBack() => Destroy(gameObject);
        protected override void OnNavTab(int dir) { page += dir; Build(); }
        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }
    }
}

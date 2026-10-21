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
        private const int Rows = 8;
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
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            float pw = W * 0.86f, ph = H * 0.9f, top = ph / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            Text("REPLAYS", new Vector2(0f, top - 1.8f), 1.1f, pw - 2f, Color.white);
            Text(Plugin.RecordReplays.Value ? $"THE LAST {Replays.KeepRecent} MATCHES AND VERSUS ROUNDS ARE KEPT; SAVE ONE TO KEEP IT FOR GOOD" : "RECORDING IS OFF (SETTINGS > RECORD REPLAYS)",
                 new Vector2(0f, top - 3.3f), 0.45f, pw - 2f, new Color(0.85f, 0.95f, 1f));
            int pages = Mathf.Max(1, (list.Count + Rows - 1) / Rows);
            page = Mathf.Clamp(page, 0, pages - 1);
            float y = top - 5.6f;
            if (list.Count == 0) Text("NO REPLAYS YET: PLAY A MATCH", new Vector2(0f, y - 4f), 0.6f, pw - 2f, Color.white);
            foreach (var e in list.Skip(page * Rows).Take(Rows))
            {
                var h = e.Header;
                Box("Row", new Vector2(-pw * 0.12f, y), new Vector2(pw * 0.72f, 2.2f), e.Saved ? new Color(0.06f, 0.28f, 0.42f, 1f) : new Color(0.02f, 0.2f, 0.34f, 1f), 2);
                Text($"<noparse>{h.Title}</noparse>", new Vector2(-pw * 0.12f, y + 0.45f), 0.48f, pw * 0.7f, Color.white, TextAlignmentOptions.Left);
                Text($"{(e.Saved ? "<color=#FFD95A>SAVED</color> · " : "")}{h.Date} · {(int)(h.Seconds / 60)}:{(int)(h.Seconds % 60):00} · <noparse>{h.Left} {h.ScoreL} - {h.ScoreR} {h.Right}</noparse>",
                     new Vector2(-pw * 0.12f, y - 0.5f), 0.38f, pw * 0.7f, new Color(0.85f, 0.95f, 1f), TextAlignmentOptions.Left);
                var entry = e;
                Button("WATCH", new Vector2(pw * 0.3f, y), new Vector2(pw * 0.09f, 1.9f), () => Watch(entry), 0.45f);
                if (e.Saved) Button("DELETE", new Vector2(pw * 0.4f, y), new Vector2(pw * 0.09f, 1.9f), () => { Replays.Delete(entry); list = Replays.List(); note = "DELETED"; Build(); }, 0.42f);
                else Button("SAVE", new Vector2(pw * 0.4f, y), new Vector2(pw * 0.09f, 1.9f), () => { Replays.Keep(entry); list = Replays.List(); note = "SAVED"; Build(); }, 0.45f);
                y -= 2.5f;
            }
            float by = -ph / 2f + 1.7f;
            if (pages > 1)
            {
                Button("<", new Vector2(-pw * 0.3f, by), new Vector2(2f, 1.8f), () => { page = (page - 1 + pages) % pages; Build(); }, 0.6f);
                Text($"PAGE {page + 1} / {pages}", new Vector2(-pw * 0.2f, by), 0.45f, 8f, Color.white);
                Button(">", new Vector2(-pw * 0.1f, by), new Vector2(2f, 1.8f), () => { page = (page + 1) % pages; Build(); }, 0.6f);
            }
            Button("BACK", new Vector2(pw * 0.2f, by), new Vector2(pw * 0.18f, 2.2f), () => Destroy(gameObject), 0.6f);
            Text(note, new Vector2(0f, by + 2.1f), 0.45f, pw - 2f, new Color(1f, 0.85f, 0.35f));
            PadHint("A: CHOOSE · B: BACK · LB / RB: PAGES", new Vector2(pw * 0.2f, by + 1.9f), pw * 0.4f);
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

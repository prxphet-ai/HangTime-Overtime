using System;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // TOURNAMENTS (title bar): this PC's community tournaments (made here, or added from a share code), each with your record
    // on it; PLAY / CONTINUE, EDIT (your own), COPY CODE, DELETE; NEW TOURNAMENT opens the editor; ADD CODE reads a code from
    // the clipboard (a pad can do it too).
    internal class TournamentMenu : OvertimePanel
    {
        private static TournamentMenu open;
        public static bool IsOpen => open != null;
        private static int page, selected;
        private static string status = "";
        private bool confirmDelete;
        private const int Rows = 7;

        public static void Open(string message = null)
        {
            if (open != null) return;
            var go = new GameObject("Overtime Tournaments");
            open = go.AddComponent<TournamentMenu>();
            if (!open.InitKit(200)) { Destroy(go); open = null; return; }
            open.HoldTitle();
            status = message ?? "";
            open.Build();
        }

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
        public static readonly Color Gold = new Color(1f, 0.85f, 0.35f), Pale = new Color(0.85f, 0.95f, 1f), Bad = new Color(1f, 0.55f, 0.5f);

        private void Build()
        {
            int keep = focus;
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops();
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            float pw = W * 0.94f, ph = H * 0.92f, top = ph / 2f, left = -pw / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            Text("COMMUNITY TOURNAMENTS", new Vector2(0f, top - 1.7f), 1.1f, pw - 2f, Color.white);
            Text("MAKE YOUR OWN TOURNAMENT, SHARE IT AS A CODE, PLAY YOUR FRIENDS'. BEAT YOUR OWN BEFORE SHARING IT ONLINE.", new Vector2(0f, top - 3.2f), 0.42f, pw - 3f, Pale);

            var list = Tournaments.All;
            int pages = Math.Max(1, (list.Count + Rows - 1) / Rows);
            page = Mathf.Clamp(page, 0, pages - 1);
            selected = Mathf.Clamp(selected, 0, Math.Max(0, list.Count - 1));
            float lw = pw * 0.52f, lx = left + 1f + lw / 2f, rowH = 2.75f, y = top - 5.6f;
            if (list.Count == 0)
                Text("NO TOURNAMENTS YET.\nNEW TOURNAMENT MAKES ONE; ADD CODE PASTES ONE A FRIEND SENT YOU.", new Vector2(lx, y - 6f), 0.55f, lw - 1f, Color.white, height: 4f, wrap: true);
            for (int i = page * Rows; i < Math.Min(list.Count, (page + 1) * Rows); i++)
            {
                var e = list[i];
                var t = Tournaments.Content(e);
                int idx = i;
                var b = Shade(Button("", new Vector2(lx, y), new Vector2(lw, rowH - 0.25f), () => Select(idx), 0.5f), i == selected ? new Color(0.85f, 0.45f, 0.08f, 1f) : ListColor);
                string name = t != null ? t.name.ToUpperInvariant() : "(DAMAGED)";
                Text($"<noparse>{name}</noparse>", new Vector2(lx, y + 0.5f), 0.55f, lw - 1.2f, Color.white, TextAlignmentOptions.Left);
                Text(t == null ? "" : Line(e, t), new Vector2(lx, y - 0.6f), 0.38f, lw - 1.2f, Pale, TextAlignmentOptions.Left);
                y -= rowH;
            }
            float py = top - 5.6f - rowH * Rows - 0.2f;
            if (pages > 1)
            {
                Button("<", new Vector2(lx - 3f, py), new Vector2(2f, 1.6f), () => { page = (page - 1 + pages) % pages; Build(); }, 0.6f);
                Text($"PAGE {page + 1}/{pages}", new Vector2(lx, py), 0.45f, 6f, Pale);
                Button(">", new Vector2(lx + 3f, py), new Vector2(2f, 1.6f), () => { page = (page + 1) % pages; Build(); }, 0.6f);
            }

            // the selected tournament
            float dx = left + 1f + lw + 1f, dw = pw - lw - 3f, cx = dx + dw / 2f;
            if (list.Count > 0) Box("Detail", new Vector2(cx, (top - 4.6f + (-ph / 2f + 4.2f)) / 2f), new Vector2(dw, top - 4.6f - (-ph / 2f + 4.2f)), new Color(0.02f, 0.2f, 0.34f, 1f), 2);
            if (list.Count > 0) Detail(list[selected], cx, dw, top - 5.6f);

            float by = -ph / 2f + 1.8f;
            Button("NEW TOURNAMENT", new Vector2(-pw * 0.3f, by), new Vector2(pw * 0.2f, 2.2f), () => { Destroy(gameObject); TournamentEditor.Open(null); }, 0.5f);
            Button("ADD CODE (PASTE)", new Vector2(-pw * 0.08f, by), new Vector2(pw * 0.2f, 2.2f), AddCode, 0.5f);
            Button("BACK", new Vector2(pw * 0.36f, by), new Vector2(pw * 0.16f, 2.2f), () => Destroy(gameObject), 0.6f);
            Text($"<noparse>{status}</noparse>", new Vector2(pw * 0.14f, by), 0.4f, pw * 0.22f, Gold, height: 2.2f, wrap: true);
            PadHint("A: CHOOSE · B: BACK · LB / RB: PAGES", new Vector2(0f, by + 1.7f), pw - 2f);
            navKeys = true;
            focus = keep >= 0 && keep < stops.Count ? keep : 0;
        }

        // "FIXED · 5 MATCHES · DIFFICULTY 6/10 · BY YOU · CLEARED 1/4 · BEST 4:12.3"
        public static string Line(TourEntry e, Tournament t)
        {
            var bits = new List<string> { t.Fixed ? "FIXED" : "DRAFT", $"{t.teams.Length} {(t.teams.Length == 1 ? "MATCH" : "MATCHES")}", $"DIFFICULTY {Tournaments.Difficulty(t)}/10" };
            bits.Add(e.mine ? "BY YOU" : e.creator.Length > 0 ? "BY " + e.creator.ToUpperInvariant() : "FROM A CODE");
            if (e.attempts > 0) bits.Add($"CLEARED {e.clears}/{e.attempts}");
            if (e.best > 0f) bits.Add("BEST " + RunClock.Format(e.best));
            return $"<noparse>{string.Join("  ·  ", bits)}</noparse>";
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
            for (int i = 0; i < t.teams.Length; i++)
            {
                var tm = t.teams[i];
                Text($"<noparse>{i + 1}. {tm.name.ToUpperInvariant()}</noparse>  <size=80%><color=#BFE6FF>{tm.@base.ToUpperInvariant()} · {tm.perks.Length}P {tm.cards.Length}C · WIN ~{Tournaments.Pct(Tournaments.WinChance(t, i))}</color></size>",
                     new Vector2(cx, y), 0.42f, dw - 1f, Color.white);
                y -= 0.95f;
            }
            if (t.Fixed) { Text($"YOUR TEAM: {t.player.perks.Length} PERKS · {t.player.cards.Length} CARDS · {t.player.levels.Sum()} STAT LEVELS", new Vector2(cx, y - 0.2f), 0.4f, dw - 1f, Pale); y -= 1.1f; }
            Text($"ESTIMATED CLEAR CHANCE {Tournaments.Pct(Tournaments.ClearChance(t))} (SIMULATOR)", new Vector2(cx, y - 0.2f), 0.4f, dw - 1f, Pale);
            if (e.mine)
            {
                var v = Tournaments.VerifyOf(e.verify, e.Hash);
                Text(v != null ? $"YOU CLEARED IT IN {RunClock.Format(v.seconds)}: READY TO PUBLISH" : "BEAT IT ONCE YOURSELF BEFORE IT CAN BE PUBLISHED",
                     new Vector2(cx, y - 1.2f), 0.4f, dw - 1f, v != null ? new Color(0.5f, 1f, 0.55f) : Gold);
            }

            // actions
            float ay = -H * 0.46f + 6.6f, bw = (dw - 2f) / 2f;
            var saved = RunSaves.CommunityRun;
            bool canContinue = saved.has && saved.community == e.Hash;
            if (canContinue)
            {
                Button($"CONTINUE (MATCH {Mathf.Clamp(saved.gameNumber - (9 - t.teams.Length) + 1, 1, t.teams.Length)}/{t.teams.Length})", new Vector2(cx - bw / 2f - 0.25f, ay + 2.4f), new Vector2(bw, 2f), () => Continue(saved), 0.42f);
                Button("PLAY FROM THE START", new Vector2(cx + bw / 2f + 0.25f, ay + 2.4f), new Vector2(bw, 2f), () => Play(e), 0.42f);
            }
            else Button(e.mine && !Tournaments.Verified(e) ? "TEST PLAY" : "PLAY", new Vector2(cx, ay + 2.4f), new Vector2(dw - 2f, 2f), () => Play(e), 0.6f);
            int n = e.mine ? 3 : 2;
            float w3 = (dw - 2f - (n - 1) * 0.5f) / n, x = cx - (dw - 2f) / 2f + w3 / 2f;
            if (e.mine) { Button("EDIT", new Vector2(x, ay), new Vector2(w3, 1.9f), () => { Destroy(gameObject); TournamentEditor.Open(e); }, 0.5f); x += w3 + 0.5f; }
            Button("COPY CODE", new Vector2(x, ay), new Vector2(w3, 1.9f), () => CopyCode(e), 0.5f); x += w3 + 0.5f;
            var del = Button(confirmDelete ? "SURE? DELETE" : "DELETE", new Vector2(x, ay), new Vector2(w3, 1.9f), () => Delete(e), 0.45f);
            if (confirmDelete) Shade(del, new Color(0.85f, 0.25f, 0.2f, 1f));
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
            status = "DELETED";
            Build();
        }

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
            else if ((kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed) && kb.vKey.wasPressedThisFrame) AddCode();
        }

        protected override void OnNavBack() => Destroy(gameObject);
        protected override void OnNavTab(int dir)
        {
            int pages = Math.Max(1, (Tournaments.All.Count + Rows - 1) / Rows);
            page = (page + dir + pages) % pages;
            Build();
        }
        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }
    }
}

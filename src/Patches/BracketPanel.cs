using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // The bracket between matches: rounds left to right, results filled in, the YOU marker slides from the match just
    // won to the next one, which then pulses. CONTINUE (A / Enter / click) closes it. Classic, Co-op and Loop show it at
    // the start of each upgrade screen (the screen waits underneath) and as CHAMPIONS on the win screen; tournaments
    // pass their own BracketModel.
    internal class BracketPanel : OvertimePanel
    {
        private static BracketPanel open;
        public static bool IsOpen => open != null;
        private BracketModel model;
        private bool pause;
        private Action onClose;
        private SpriteRenderer marker, pulse;
        private Vector3 markFrom, markTo;
        private float openedAt;
        private readonly List<(Behaviour b, bool was)> held = new List<(Behaviour, bool)>();
        private readonly List<Collider2D> heldColliders = new List<Collider2D>();

        public static void Show(BracketModel m, bool pauseScene, Action onClose = null)
        {
            if (open != null || m == null || m.Rounds.Count == 0) return;
            var go = new GameObject("Overtime Bracket");
            open = go.AddComponent<BracketPanel>();
            open.model = m; open.pause = pauseScene; open.onClose = onClose;
            if (!open.InitKit(300)) { Destroy(go); open = null; return; }
            open.Build();
            Plugin.Log.LogInfo($"Bracket: {m.Title} ({m.Sub})");
        }

        private void Build()
        {
            openedAt = Time.unscaledTime;
            HoldScene();
            if (pause) Time.timeScale = 0f;
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.7f), 0);
            int maxMatches = model.Rounds.Max(r => r.Matches.Count);
            float pw = W * 0.94f, ph = Mathf.Min(H * 0.9f, 17f + maxMatches * 4.2f), top = ph / 2f, left = -pw / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            Text(model.Title, new Vector2(0f, top - 2f), model.Champion ? 1.4f : 1.1f, pw - 2f, model.Champion ? new Color(1f, 0.85f, 0.3f) : Color.white);
            Text($"<noparse>{model.Sub}</noparse>", new Vector2(0f, top - 4f), 0.55f, pw - 2f, new Color(1f, 0.85f, 0.35f));

            int n = model.Rounds.Count;
            float colW = (pw - 2f) / n, boxW = colW - 1.3f, boxH = 3.8f;
            float y0 = top - 7.6f, y1 = -ph / 2f + 4.6f;
            var ys = new List<float[]>();
            for (int r = 0; r < n; r++)
            {
                var round = model.Rounds[r];
                int k = round.Matches.Count;
                var y = new float[k];
                bool tree = r > 0 && ys[r - 1].Length == k * 2;
                for (int i = 0; i < k; i++)
                    y[i] = tree ? (ys[r - 1][2 * i] + ys[r - 1][2 * i + 1]) / 2f : (k == 1 ? (y0 + y1) / 2f : y0 - (i + 0.5f) * (y0 - y1) / k);
                ys.Add(y);
            }
            Vector2 MatchAt(int r, int i) => new Vector2(left + 1f + colW * (r + 0.5f), ys[r][i]);

            for (int r = 0; r < n; r++)
            {
                var round = model.Rounds[r];
                float cx = left + 1f + colW * (r + 0.5f);
                Text($"<noparse>{round.Name}</noparse>", new Vector2(cx, top - 6f), 0.5f, colW - 0.3f, new Color(0.85f, 0.95f, 1f));
                for (int i = 0; i < round.Matches.Count; i++)
                {
                    var m = round.Matches[i];
                    var at = MatchAt(r, i);
                    bool next = r == model.ToRound && (m.A == model.You || m.B == model.You);
                    if (next) pulse = Box("Next", at, new Vector2(boxW + 0.6f, boxH + 0.6f), new Color(1f, 0.85f, 0.25f, 0f), 2);
                    Box("Match", at, new Vector2(boxW, boxH), m.Winner >= 0 ? new Color(0.03f, 0.22f, 0.38f, 1f) : new Color(0.02f, 0.17f, 0.3f, 1f), 3);
                    Box("Split", at, new Vector2(boxW - 0.6f, 0.08f), new Color(1f, 1f, 1f, 0.15f), 4);
                    Text(Row(m.A, m.Winner, 0), at + new Vector2(0f, boxH / 4f), 0.52f, boxW - 0.5f, Color.white);
                    Text(Row(m.B, m.Winner, 1), at - new Vector2(0f, boxH / 4f), 0.52f, boxW - 0.5f, Color.white);
                    if (!string.IsNullOrEmpty(m.Note)) Text(m.Note, at - new Vector2(0f, boxH / 2f + 0.6f), 0.44f, boxW, new Color(1f, 0.78f, 0.29f));
                    // connector to the match this one feeds
                    if (r + 1 < n)
                    {
                        int to = model.Rounds[r + 1].Matches.Count * 2 == round.Matches.Count ? i / 2 : Mathf.Min(i, model.Rounds[r + 1].Matches.Count - 1);
                        var dst = MatchAt(r + 1, to);
                        float x0 = at.x + boxW / 2f, x1 = dst.x - boxW / 2f, xm = (x0 + x1) / 2f;
                        var line = m.Winner >= 0 ? new Color(1f, 0.85f, 0.35f, 0.9f) : new Color(1f, 1f, 1f, 0.35f);
                        Box("Line", new Vector2((x0 + xm) / 2f, at.y), new Vector2(xm - x0, 0.14f), line, 2);
                        if (Mathf.Abs(dst.y - at.y) > 0.01f) Box("Line", new Vector2(xm, (at.y + dst.y) / 2f), new Vector2(0.14f, Mathf.Abs(dst.y - at.y)), line, 2);
                        Box("Line", new Vector2((xm + x1) / 2f, dst.y), new Vector2(x1 - xm, 0.14f), line, 2);
                    }
                }
            }

            // the YOU marker: from the match just won to the next (or sitting on the final for a champion)
            int MatchOf(int r) => r >= 0 && r < n ? model.Rounds[r].Matches.FindIndex(x => x.A == model.You || x.B == model.You) : -1;
            int fromR = model.FromRound >= 0 ? model.FromRound : model.ToRound >= 0 ? model.ToRound : n - 1;
            int toR = model.ToRound >= 0 ? model.ToRound : fromR;
            int fi = MatchOf(fromR), ti = MatchOf(toR);
            if (fi >= 0 && ti >= 0)
            {
                var off = new Vector2(-boxW / 2f + 0.2f, boxH / 2f + 0.55f);
                marker = Box("You", MatchAt(fromR, fi) + off, new Vector2(2.6f, 1.1f), new Color(0.96f, 0.55f, 0.12f, 1f), 10);
                var label = Text("YOU", Vector2.zero, 0.45f, 2.6f, Color.white, o: 11);
                label.transform.SetParent(marker.transform, true);
                label.transform.localPosition = new Vector3(0f, 0f, -0.01f);
                markFrom = marker.transform.localPosition;
                markTo = (Vector3)(MatchAt(toR, ti) + off) + new Vector3(0f, 0f, markFrom.z);
            }
            Button("CONTINUE", new Vector2(0f, -ph / 2f + 1.8f), new Vector2(pw * 0.2f, 2.3f), Close, 0.65f);
            PadHint("A: CONTINUE", new Vector2(0f, -ph / 2f + 3.5f), pw - 2f);
            navKeys = true;
            focus = stops.Count - 1;
        }

        // "TEAM" for a match row: the winner gold, the loser struck through and dim
        private static string Row(string name, int winner, int side)
        {
            string n = $"<noparse>{name.ToUpperInvariant()}</noparse>";
            if (winner < 0) return n;
            return winner == side ? $"<color=#FFD95A>{n}</color>" : $"<color=#7F93A8><s>{n}</s></color>";
        }

        private void Update()
        {
            float t = Time.unscaledTime - openedAt;
            if (marker != null)
            {
                float k = Mathf.Clamp01((t - 0.6f) / 0.9f);
                k = k * k * (3f - 2f * k);
                marker.transform.localPosition = Vector3.Lerp(markFrom, markTo, k) + new Vector3(0f, Mathf.Sin(k * Mathf.PI) * 1.2f, 0f);
            }
            if (pulse != null && t > 1.5f) { var c = pulse.color; c.a = 0.35f + 0.35f * Mathf.Sin((t - 1.5f) * 5f); pulse.color = c; }
            if (pause) Time.timeScale = 0f;                              // anything resetting it while the screen waits
            if (t < 0.4f) return;                                       // the press that ended the match / opened it
            if (NavUpdate()) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)) Close();
        }

        protected override void OnNavBack() => Close();

        private void Close() { if (this != null && gameObject != null) Destroy(gameObject); }

        // the scene's own buttons stop taking clicks and the pad's A while the bracket is up
        private void HoldScene()
        {
            foreach (var g in FindObjectsByType<ButtonGroup>(FindObjectsSortMode.None))
                if (g.enabled) { held.Add((g, true)); g.enabled = false; }
            foreach (var b in FindObjectsByType<ButtonController>(FindObjectsSortMode.None))
                foreach (var c in b.GetComponents<Collider2D>().Where(c => c.enabled)) { c.enabled = false; heldColliders.Add(c); }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            foreach (var (b, was) in held) if (b != null) b.enabled = was;
            foreach (var c in heldColliders) if (c != null) c.enabled = true;
            if (pause) Time.timeScale = 1f;
            if (open == this) open = null;
            var cb = onClose; onClose = null;
            cb?.Invoke();
        }

        // ------------------------------------------------------------ when it shows

        private static bool BracketMode => (RunState.Mode == RunMode.Normal || RunState.Mode == RunMode.Loop) && !VersusState.Active && !TutorialManager.onTutorial;

        [HarmonyPatch(typeof(UpgradeManager), "Start")]
        private static class BetweenMatches
        {
            private static int won;

            // the match just won, before the reward screen may swap in a stand-in match number
            [HarmonyPrefix]
            private static void Prefix() => won = GameManager.gameNumber;

            [HarmonyPostfix]
            private static void Postfix()
            {
                if (!BracketMode || won < 1 || won >= 8) return;
                int slot = won;
                Runner.Instance.StartCoroutine(After(0.35f, () => Show(Bracket.Classic(slot), true)));
            }
        }

        // nothing behind the bracket reacts, including buttons that appear after it opened (the win screen's)
        [HarmonyPatch(typeof(ButtonGroup), "Select")]
        private static class HoldGroups
        {
            [HarmonyPrefix]
            private static bool Prefix() => !IsOpen && !CoopPick.IsOpen;
        }

        [HarmonyPatch(typeof(ButtonController), "Press")]
        private static class HoldButtons
        {
            [HarmonyPrefix]
            private static bool Prefix() => !IsOpen && !CoopPick.IsOpen;
        }

        // the upgrade screen's skip (the pad's START) waits until the bracket is closed
        [HarmonyPatch(typeof(UpgradeManager), "SkipUpgrade")]
        private static class HoldSkip
        {
            [HarmonyPrefix]
            private static bool Prefix() => !IsOpen && !CoopPick.IsOpen;
        }

        [HarmonyPatch(typeof(WinManager), "Start")]
        private static class Champions
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (!BracketMode || RunState.Mode != RunMode.Normal) return;
                Runner.Instance.StartCoroutine(After(1.2f, () => Show(Bracket.Classic(8), false)));
            }
        }

        private static IEnumerator After(float seconds, Action a)
        {
            yield return new WaitForSecondsRealtime(seconds);
            a();
        }
    }
}

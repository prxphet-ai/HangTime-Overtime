using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HangtimeOvertime.Patches
{
    // A menu button action from code (the game's buttons call OnClick.Click).
    internal class PopupClick : OnClick
    {
        public Action Action;
        public override void Click() => Action?.Invoke();
    }

    // Title screen: a saved Infinite/Classic run shows on its button; clicking that button offers
    // CONTINUE / NEW RUN / BACK, and NEW RUN asks before the save is deleted.
    internal static class ContinueMenu
    {
        public static bool Bypass;                 // the next TitleButton click starts a fresh run
        private static GameObject popup;
        public static bool IsOpen => ChoicePanel.IsOpen;
        private static readonly List<GameObject> popupButtons = new List<GameObject>();   // live next to the menu buttons (same scale)
        private static GameObject savedOpponent;

        private static readonly AccessTools.FieldRef<TitleButton, TitleController> titleOf = AccessTools.FieldRefAccess<TitleButton, TitleController>("title");
        private static readonly AccessTools.FieldRef<ButtonController, OnClick> onClickOf = AccessTools.FieldRefAccess<ButtonController, OnClick>("onClick");

        public static bool IsInfinite(TitleButton b) => b.GetComponent<InfiniteButtonMarker>() != null;
        public static int Players(TitleButton b) => Traverse.Create(b).Field("myNumberOfPlayers").GetValue<int>();

        // ------------------------------------------------------------ the mode buttons' second line

        public static void Label(GameObject classic, GameObject infinite)
        {
            if (classic != null && RunSaves.Has(false)) SetSub(classic, "CONTINUE · " + RunSaves.Where(RunSaves.Classic));
            if (infinite != null && RunSaves.Has(true)) SetSub(infinite, "CONTINUE · " + RunSaves.Where(RunSaves.Infinite));
            if (RunSaves.LoadProblem != null && classic != null)
            {
                Notice(classic, RunSaves.LoadProblem + " Starting fresh.");
                RunSaves.LoadProblem = null;
            }
        }

        private static void SetSub(GameObject button, string text)
        {
            var labels = button.GetComponentsInChildren<TextMeshPro>(true).OrderBy(l => l.name).ToArray();
            if (labels.Length > 1) labels[1].text = text.ToUpperInvariant();
        }

        private static void Notice(GameObject near, string text)
        {
            var src = near.GetComponentsInChildren<TextMeshPro>(true).OrderBy(l => l.name).Skip(1).FirstOrDefault();
            if (src == null) return;
            var t = UnityEngine.Object.Instantiate(src.gameObject, near.transform.parent).GetComponent<TextMeshPro>();
            t.name = "Overtime save notice";
            t.transform.position = near.transform.position + new Vector3(0f, 1.7f, 0f);
            t.text = text.ToUpperInvariant();
            t.color = new Color(1f, 0.85f, 0.4f);
            t.autoSizeTextContainer = false;
                t.textWrappingMode = TextWrappingModes.Normal;
            t.rectTransform.sizeDelta = new Vector2(t.rectTransform.sizeDelta.x * 3f, t.rectTransform.sizeDelta.y * 2f);
            UnityEngine.Object.Destroy(t.gameObject, 8f);
        }

        // ------------------------------------------------------------ co-op: CLASSIC or INFINITE

        public static bool CoopInfinite;              // the next co-op start is an Infinite run

        public static void ShowCoop(TitleButton from)
        {
            var snap = RunSaves.CoopRun;
            int best = Records.Data.coopInfiniteBest;
            Build(from, "CO-OP\n<size=70%>TWO PLAYERS ON ONE PC</size>",
                ("CLASSIC", "THE TOURNAMENT", () => { Close(from); Bypass = true; CoopInfinite = false; from.Click(); }),
                ("INFINITE", snap.has ? "CONTINUE · " + RunSaves.Where(snap) : best > 0 ? $"BEST: {best} WINS" : "ENDLESS, TOGETHER", () => CoopInfiniteChoice(from)),
                ("BACK", "-", () => Close(from)));
        }

        private static void CoopInfiniteChoice(TitleButton from)
        {
            var snap = RunSaves.CoopRun;
            if (!snap.has) { StartCoopInfinite(from); return; }
            Build(from, $"SAVED CO-OP INFINITE RUN\n<size=70%>{RunSaves.Summary(snap).ToUpperInvariant()}\n{RunSaves.PerkCount(snap)} ABILITIES · {snap.savedAt}</size>",
                ("CONTINUE", "-", () => Continue(from, snap)),
                ("NEW RUN", "-", () => Build(from, $"START A NEW CO-OP INFINITE RUN?\n<size=70%>THIS DELETES THE SAVED ONE:\n{RunSaves.Summary(snap).ToUpperInvariant()}</size>",
                    ("START OVER", "-", () => { RunSaves.ClearCoop(); StartCoopInfinite(from); }),
                    ("NO, KEEP IT", "-", () => CoopInfiniteChoice(from)))),
                ("BACK", "-", () => ShowCoop(from)));
        }

        private static void StartCoopInfinite(TitleButton from)
        {
            Close(from);
            CoopInfinite = true;
            Bypass = true;
            from.Click();
        }

        // ------------------------------------------------------------ the choice panel

        public static void Show(TitleButton from, bool infinite)
        {
            DestroyPopup();
            var snap = infinite ? RunSaves.Infinite : RunSaves.Classic;
            string mode = infinite ? "INFINITE" : "CLASSIC";
            Build(from, $"SAVED {mode} RUN\n<size=70%>{RunSaves.Summary(snap).ToUpperInvariant()}\n{RunSaves.PerkCount(snap)} {(RunSaves.PerkCount(snap) == 1 ? "ABILITY" : "ABILITIES")} · {RunSaves.BoostCount(snap)} STAT {(RunSaves.BoostCount(snap) == 1 ? "BOOST" : "BOOSTS")} · {snap.savedAt}</size>",
                ("CONTINUE", "-", () => Continue(from, snap)),
                ("NEW RUN", "-", () => Confirm(from, infinite)),
                ("BACK", "-", () => Close(from)));
        }

        private static void Confirm(TitleButton from, bool infinite)
        {
            string mode = infinite ? "INFINITE" : "CLASSIC";
            Build(from, $"START A NEW {mode} RUN?\n<size=70%>THIS DELETES YOUR SAVED RUN:\n{RunSaves.Summary(infinite ? RunSaves.Infinite : RunSaves.Classic).ToUpperInvariant()}</size>",
                ("START OVER", "-", () => { RunSaves.Clear(infinite); Close(from); Bypass = true; from.Click(); }),
                ("NO, KEEP IT", "-", () => Show(from, infinite)));
        }

        private static void Close(TitleButton from)
        {
            DestroyPopup();
            var group = from.GetComponentInParent<ButtonGroup>(true);
            if (group != null)
                foreach (var b in group.buttons.Where(b => b != null)) { b.interactable = true; b.wasClicked = false; }
        }

        // the popup: a kit panel (ChoicePanel) with the header's first line on the ribbon, the rest under it, and the choices
        private static void Build(TitleButton from, string header, params (string text, string sub, Action act)[] buttons)
        {
            DestroyPopup();
            var lines = header.Split('\n');
            string title = lines[0], sub = string.Join("\n", lines.Skip(1).Select(l => System.Text.RegularExpressions.Regex.Replace(l, "<[^>]+>", "")));
            var back = buttons.FirstOrDefault(x => x.text == "BACK" || x.text.StartsWith("NO,")).act ?? (() => Close(from));
            ChoicePanel.Show(title, sub, buttons, back);
        }

        private static void DestroyPopup() => ChoicePanel.CloseNow();

        // a rounded box that stretches without blurring its corners (9-sliced)
        private static Sprite rounded;
        internal static Sprite Rounded()
        {
            if (rounded != null) return rounded;
            const int n = 32, r = 10;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontUnloadUnusedAsset, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Max(r - x - 0.5f, x + 0.5f - (n - r))), dy = Mathf.Max(0, Mathf.Max(r - y - 0.5f, y + 0.5f - (n - r)));
                    float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            rounded = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 16f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
            rounded.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return rounded;
        }

        // ------------------------------------------------------------ resuming

        // the DAILY menu resumes its own saved run the same way
        public static void Resume(TitleButton from, RunSnapshot snap) => Continue(from, snap);

        private static void Continue(TitleButton from, RunSnapshot snap)
        {
            if (!RunSaves.Apply(snap, out var problem)) { Close(from); return; }
            if (problem != null) Plugin.Log.LogWarning(problem);
            DestroyPopup();
            var gm = GameManager.Instance;
            gm.numberOfPlayers = Math.Max(1, snap.players);
            var title = titleOf(from);
            if (title != null) from.StartCoroutine(title.StartGame());
            gm.ResetGame();
            PauseController.paused = false;
            if (snap.stage == "upgrade") from.StartCoroutine(LoadUpgrade(snap));
            else from.StartCoroutine(gm.LoadGame());
        }

        // The upgrade screen reads the opponent's name for its dialogue; a stand-in carries it over.
        private static IEnumerator LoadUpgrade(RunSnapshot snap)
        {
            yield return new WaitForSeconds(1.5f);
            var gm = GameManager.Instance;
            gm.transition.Play();
            yield return new WaitForSeconds(0.5f);
            TitleController.onTitle = false;
            if (savedOpponent != null) UnityEngine.Object.Destroy(savedOpponent);
            savedOpponent = new GameObject("Saved opponent");
            UnityEngine.Object.DontDestroyOnLoad(savedOpponent);
            var ot = savedOpponent.AddComponent<OpponentTeam>();
            ot.teamName = (snap.opponentName ?? "").Replace("(Clone)", "").Trim();
            ot.gotPointDialogue = new List<string>();
            ot.lostPointDialogue = new List<string>();
            gm.opponentTeam = ot;
            TeamRoster.RestoreCurrent(snap.opponentKey);
            SceneManager.LoadScene("Upgrade");
        }

        public static void DropStandIn()
        {
            if (savedOpponent != null) UnityEngine.Object.Destroy(savedOpponent);
            savedOpponent = null;
        }
    }

    // The title screen's choice popups (CO-OP: CLASSIC / INFINITE, a saved run: CONTINUE / NEW RUN, the confirmations) in the
    // menus' look: the gold ribbon, the details under it, the first choice as the gold sign and the rest as buttons.
    internal class ChoicePanel : OvertimePanel
    {
        private static ChoicePanel open;
        public static bool IsOpen => open != null;
        private Action back;

        public static void Show(string title, string sub, (string text, string sub, Action act)[] buttons, Action back)
        {
            CloseNow();
            var go = new GameObject("Overtime Choice");
            open = go.AddComponent<ChoicePanel>();
            if (!open.InitKit(240)) { Destroy(go); open = null; return; }
            open.HoldTitle();
            open.back = back;
            open.Build(title, sub, buttons);
        }

        public static void CloseNow() { if (open != null) { var o = open; open = null; Destroy(o.gameObject); } }

        private void Build(string title, string sub, (string text, string sub, Action act)[] buttons)
        {
            int n = buttons.Length;
            int subLines = string.IsNullOrEmpty(sub) ? 0 : sub.Split('\n').Length;
            float subH = subLines * 32f, h = 100f + subH + n * 100f, top = 450f - h / 2f;
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0.024f, 0.086f, 0.17f, 0.6f), 0);
            Box("Panel", P(800, top + 20f + h / 2f), S(720, h), UiArt.Navy, 1);
            float rw = Mathf.Clamp(title.Length * 28f + 120f, 360f, 760f);
            var ribbon = Rect("Ribbon", 800 - rw / 2f, top - 30, 800 + rw / 2f, top + 44, UiArt.Gold, 2, true);
            Tilt(ribbon, -1.5f);
            Tilt(TC($"<noparse>{title}</noparse>", 800, top + 7, 40, UiArt.Dark, rw - 40f), -1.5f);
            if (subLines > 0) TW($"<noparse>{sub}</noparse>", 800, top + 76 + subH / 2f, 24, new Color(0.78f, 0.88f, 1f), 660, subH + 10f);
            float y = top + 80f + subH;
            for (int i = 0; i < n; i++)
            {
                var (text, bsub, act) = buttons[i];
                bool hasSub = !string.IsNullOrEmpty(bsub) && bsub != "-";
                var a = act;
                if (i == 0)
                {
                    GoldSign(hasSub ? "" : text, 540, y, 1060, y + 80, () => { a(); }, 40, text == "BACK" ? "back" : "play");
                    if (hasSub) Tilt(TC(text, 810, y + 30, 38, UiArt.Dark, 420), -0.8f);
                }
                else
                {
                    Btn("", 560, y + 4, 1040, y + 76, () => { a(); });
                    TC(text, 800, hasSub ? y + 28 : y + 40, 34, UiArt.Ink, 460);
                }
                if (hasSub) TC($"<noparse>{bsub.ToUpperInvariant()}</noparse>", 800, y + 60, 18, i == 0 ? UiArt.Dark : new Color(0.78f, 0.88f, 1f), 460);
                y += 100f;
            }
            PadHint("A: CHOOSE   B: BACK", P(800, top + h + 40), 400 / 25f * KX);
            navKeys = true;
            focus = 0;
        }

        private void Update()
        {
            if (NavUpdate()) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) back?.Invoke();
        }

        protected override void OnNavBack() => back?.Invoke();
        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }
    }
}

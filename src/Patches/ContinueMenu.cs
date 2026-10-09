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
            t.enableWordWrapping = true;
            t.rectTransform.sizeDelta = new Vector2(t.rectTransform.sizeDelta.x * 3f, t.rectTransform.sizeDelta.y * 2f);
            UnityEngine.Object.Destroy(t.gameObject, 8f);
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

        private static void Build(TitleButton from, string header, params (string text, string sub, Action act)[] buttons)
        {
            DestroyPopup();
            var template = from.gameObject;
            var baseSr = template.GetComponent<SpriteRenderer>();
            var cam = Camera.main;
            float z = template.transform.position.z - 0.3f;
            var center = new Vector3(cam != null ? cam.transform.position.x : 0f, cam != null ? cam.transform.position.y : 0f, z);
            popup = new GameObject("Overtime Continue");
            popup.transform.position = center;
            int order = baseSr.sortingOrder + 40;

            SpriteRenderer Box(string name, Vector3 at, Vector2 size, Color color, int o)
            {
                var sr = new GameObject(name).AddComponent<SpriteRenderer>();
                sr.transform.SetParent(popup.transform, false);
                sr.transform.position = at;
                sr.sprite = Rounded();
                sr.drawMode = SpriteDrawMode.Sliced;
                sr.size = size;
                sr.color = color;
                sr.sortingLayerID = baseSr.sortingLayerID;
                sr.sortingOrder = o;
                return sr;
            }

            float h = cam != null ? cam.orthographicSize * 2f : 60f, w = h * (cam != null ? cam.aspect : 1.8f);
            Box("Dim", center, new Vector2(w * 1.2f, h * 1.2f), new Color(0f, 0.1f, 0.2f, 0.5f), order);

            float step = 3.3f, headerH = 6f;
            float panelH = headerH + buttons.Length * step + 1.2f, panelW = 20f;
            float panelTop = center.y + panelH / 2f;
            Box("Panel", center, new Vector2(panelW, panelH), new Color(0.05f, 0.42f, 0.66f, 0.97f), order + 1);

            // header text, copied from the menu button label (the game font), at that label size in the world
            var label = template.GetComponentsInChildren<TextMeshPro>(true).OrderBy(l => l.name).FirstOrDefault();
            if (label != null)
            {
                var t = UnityEngine.Object.Instantiate(label.gameObject, popup.transform).GetComponent<TextMeshPro>();
                t.name = "Header";
                var ls = label.transform.lossyScale;
                t.transform.localScale = ls;
                t.transform.position = new Vector3(center.x, panelTop - headerH / 2f, z - 0.02f);
                t.text = header;
                t.alignment = TextAlignmentOptions.Center;
                t.enableWordWrapping = true;
                t.rectTransform.sizeDelta = new Vector2((panelW - 2f) / ls.x, (headerH - 1f) / ls.y);
                t.enableAutoSizing = true;
                t.fontSizeMin = label.fontSize * 0.3f;
                t.fontSizeMax = label.fontSize * 0.9f;
                var tr = t.GetComponent<Renderer>();
                tr.sortingLayerID = baseSr.sortingLayerID;
                tr.sortingOrder = order + 5;
            }

            var group = popup.AddComponent<ButtonGroup>();
            group.buttons = new List<ButtonController>();
            // the visible bar of a menu button is its child "Sprite"; the button's own renderer is a small square kept hidden behind it
            var bar = template.GetComponentsInChildren<SpriteRenderer>(true).FirstOrDefault(r => r.name == "Sprite");
            var bsize = bar != null ? bar.bounds.size : baseSr.bounds.size;
            for (int i = 0; i < buttons.Length; i++)
            {
                var pos = new Vector3(center.x, panelTop - headerH - step * (i + 0.5f), z - 0.01f);
                Box("Backing", pos, new Vector2(bsize.x, bsize.y), new Color(0.16f, 0.66f, 0.9f, 1f), order + 2);
                var b = UnityEngine.Object.Instantiate(template, template.transform.parent);
                popupButtons.Add(b);
                b.name = "Overtime " + buttons[i].text;
                b.transform.position = pos + new Vector3(0f, 0f, -0.01f);
                foreach (var m in b.GetComponents<MonoBehaviour>().Where(m => m is TitleButton || m is InfiniteButtonMarker)) UnityEngine.Object.DestroyImmediate(m);
                var click = b.AddComponent<PopupClick>();
                click.Action = buttons[i].act;
                var ctrl = b.GetComponent<ButtonController>();
                onClickOf(ctrl) = click;
                ctrl.interactable = true;
                ctrl.wasClicked = false;
                ctrl.group = group;
                group.buttons.Add(ctrl);
                MenuPatches.SetButtonLabels(b, buttons[i].text, buttons[i].sub);
                foreach (var r in b.GetComponentsInChildren<Renderer>(true))
                {
                    r.sortingLayerID = baseSr.sortingLayerID;
                    if (r.gameObject == b) r.enabled = false;            // the hidden square
                    else r.sortingOrder = r is SpriteRenderer ? order + 3 : order + 6;
                }
            }
        }

        private static void DestroyPopup()
        {
            if (popup != null) UnityEngine.Object.Destroy(popup);
            popup = null;
            foreach (var b in popupButtons) if (b != null) UnityEngine.Object.Destroy(b);
            popupButtons.Clear();
        }

        // a rounded box that stretches without blurring its corners (9-sliced)
        private static Sprite rounded;
        private static Sprite Rounded()
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

        private static void Continue(TitleButton from, RunSnapshot snap)
        {
            if (!RunSaves.Apply(snap, out var problem)) { Close(from); return; }
            if (problem != null) Plugin.Log.LogWarning(problem);
            DestroyPopup();
            var gm = GameManager.Instance;
            gm.numberOfPlayers = 1;
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
}

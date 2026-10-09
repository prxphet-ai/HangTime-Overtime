using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HangtimeOvertime.Generated;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HangtimeOvertime.Patches
{
    // Shared building blocks for the mod's full-screen panels: rounded boxes, text in the game's font, buttons.
    internal abstract class OvertimePanel : MonoBehaviour
    {
        protected int layer, order;
        protected TextMeshPro fontSource;
        protected float W, H;
        protected Vector3 textScale = new Vector3(0.02f, 0.02f, 1f);
        private float baseOrtho;

        // the camera zooms (match point, kills): the panel keeps filling the same part of the screen
        protected virtual void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null && baseOrtho > 0f) transform.localScale = Vector3.one * (cam.orthographicSize / baseOrtho);
        }

        // Lays the panel over the camera's view (it follows camera shake and zoom).
        protected bool InitKit(int orderBase)
        {
            var cam = Camera.main;
            // the game's menu-button label: its font, size and scale (the same text the title menu uses)
            fontSource = Resources.FindObjectsOfTypeAll<TitleButton>().Where(b => b.gameObject.scene.IsValid())
                .SelectMany(b => b.GetComponentsInChildren<TextMeshPro>(true)).OrderBy(t => t.name).FirstOrDefault(t => t.font != null)
                ?? Resources.FindObjectsOfTypeAll<TextMeshPro>().FirstOrDefault(t => t.font != null && t.gameObject.scene.IsValid());
            if (fontSource != null)
            {
                textScale = fontSource.transform.lossyScale;
                if (textScale.x < 0.001f) textScale = new Vector3(0.02f, 0.02f, 1f);
            }
            if (cam == null || fontSource == null) return false;
            layer = SortingLayer.NameToID("Transition");
            order = orderBase;
            H = cam.orthographicSize * 2f; W = H * cam.aspect;
            baseOrtho = cam.orthographicSize;
            transform.SetParent(cam.transform, false);
            transform.localPosition = new Vector3(0f, 0f, 1f);
            transform.localRotation = Quaternion.identity;
            return true;
        }

        protected SpriteRenderer Box(string name, Vector2 at, Vector2 size, Color color, int o)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.transform.localPosition = new Vector3(at.x, at.y, -0.001f * o);
            sr.sprite = ContinueMenu.Rounded();
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = size;
            sr.color = color;
            sr.sortingLayerID = layer;
            sr.sortingOrder = order + o;
            return sr;
        }

        protected TextMeshPro Text(string s, Vector2 at, float size, float width, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center, int o = 8, float height = 3f, bool wrap = false)
        {
            var t = Instantiate(fontSource.gameObject, transform).GetComponent<TextMeshPro>();
            t.gameObject.SetActive(true);
            t.name = "Text";
            var ls = textScale;
            t.transform.localScale = ls;
            t.transform.localPosition = new Vector3(at.x, at.y, -0.01f * o);
            t.text = s;
            t.alignment = align;
            t.autoSizeTextContainer = false;                     // the copied label fits its box to the text; wrapping needs a fixed box
            t.margin = Vector4.zero;                             // and it carries negative side margins that widen the box
            t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.enableAutoSizing = false;
            t.fontSize = fontSource.fontSize * size;
            t.rectTransform.sizeDelta = new Vector2(width / ls.x, height / ls.y);
            t.color = color;
            var r = t.GetComponent<Renderer>();
            r.sortingLayerID = layer;
            r.sortingOrder = order + o;
            return t;
        }

        protected SpriteRenderer Button(string label, Vector2 at, Vector2 size, Action act, float textSize = 0.8f)
        {
            var normal = new Color(0.16f, 0.66f, 0.9f, 1f);
            var sr = Box("Button " + label, at, size, normal, 5);
            var col = sr.gameObject.AddComponent<BoxCollider2D>();
            col.size = size;
            var w = sr.gameObject.AddComponent<CreatorWidget>();
            w.Box = sr; w.Normal = normal; w.Hover = new Color(0.4f, 0.8f, 1f, 1f); w.OnClick = act;
            Text(label, at, textSize, size.x, Color.white);
            return sr;
        }
    }

    // Between rounds: the round score, both builds, and the loser's pick (only the loser's device can choose).
    // End of match: both builds and CONTINUE / REMATCH / MENU.
    internal class VersusOverlay : OvertimePanel
    {
        private static VersusOverlay open;
        private static GameObject notice;
        private int picker = -1;                      // side choosing an upgrade, or -1
        private List<VersusOffer> offers;
        private readonly List<SpriteRenderer> offerBoxes = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> endButtons = new List<SpriteRenderer>();
        private readonly List<Action> endActions = new List<Action>();
        private int sel, endSel;
        private bool done;

        public static void ShowRound(int winner, bool matchOver)
        {
            if (open != null) Destroy(open.gameObject);
            var go = new GameObject("Overtime Versus Overlay");
            open = go.AddComponent<VersusOverlay>();
            if (!open.InitKit(200)) { Plugin.Log.LogWarning("Versus overlay: no camera/font; skipping to the next round"); Destroy(go); VersusFlow.NextRound(); return; }
            open.Build(winner, matchOver);
        }

        public static void Notice(string text)
        {
            if (notice != null) Destroy(notice);
            if (text == null) return;
            var go = new GameObject("Overtime Versus Notice");
            var p = go.AddComponent<NoticePanel>();
            if (!p.Show(text)) Destroy(go);
            notice = go;
        }

        private class NoticePanel : OvertimePanel
        {
            public bool Show(string text)
            {
                if (!InitKit(260)) return false;
                Box("Notice", new Vector2(0f, 0f), new Vector2(W * 0.7f, 6f), new Color(0.6f, 0.12f, 0.12f, 0.95f), 0);
                Text(text, new Vector2(0f, 0f), 0.9f, W * 0.66f, Color.white, height: 5.5f, wrap: true);
                return true;
            }
        }

        private void Build(int winner, bool matchOver)
        {
            int loser = 1 - winner;
            int[] w = VersusState.RoundWins;
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.08f, 0.16f, 0.62f), 0);
            float pw = W * 0.88f, ph = H * 0.86f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            float top = ph / 2f;
            string head = matchOver ? $"P{winner + 1} WINS THE MATCH!" : $"ROUND {VersusState.Round} TO P{winner + 1}";
            Text(head, new Vector2(0f, top - 2f), 1.3f, pw - 2f, Color.white);
            Text($"P1  {w[0]} - {w[1]}  P2", new Vector2(0f, top - 4.6f), 1.6f, pw - 2f, new Color(1f, 0.85f, 0.35f));
            Text(matchOver ? "" : $"FIRST TO {VersusState.Target} ROUNDS", new Vector2(0f, top - 6.6f), 0.6f, pw - 2f, new Color(0.85f, 0.95f, 1f));

            // both builds, left and right
            for (int side = 0; side < 2; side++)
            {
                float x = (side == 0 ? -1f : 1f) * pw * 0.36f;
                Text($"P{side + 1} - {VersusState.TeamName(side, VersusState.Setup.Team[side]).ToUpperInvariant()}", new Vector2(x, top - 8.6f), 0.7f, pw * 0.26f, Color.white);
                var build = VersusState.Build(side);
                string list = build.Count == 0 ? "NO UPGRADES YET" : string.Join("\n", build.Take(12).Select(b => b.ToUpperInvariant())) + (build.Count > 12 ? $"\n+{build.Count - 12} MORE" : "");
                Text(list, new Vector2(x, top - 8.6f - 1.4f - 7f), 0.68f, pw * 0.26f, new Color(0.85f, 0.95f, 1f), TextAlignmentOptions.Top, height: 14f, wrap: true);
            }

            if (!matchOver)
            {
                picker = loser;
                offers = VersusState.Offer(loser);
                Plugin.Log.LogInfo($"Versus: P{loser + 1} offered " + string.Join(" | ", offers.Select((o, i) => $"{i}: {o.Title} ({o.Kind})")));
                var slot = VersusState.Setup.Human(loser);
                Text($"P{loser + 1} PICKS AN UPGRADE  <size=70%>({slot?.Input?.Label})</size>", new Vector2(0f, top - 8.6f), 0.8f, pw * 0.42f, Color.white);
                float cw = Mathf.Min(pw * 0.15f, 9f), ch = ph * 0.45f, gap = cw * 0.12f;
                for (int i = 0; i < offers.Count; i++)
                {
                    int idx = i;
                    float x = (i - (offers.Count - 1) / 2f) * (cw + gap);
                    float y = -ph * 0.08f;
                    var o = offers[i];
                    var box = Box("Offer", new Vector2(x, y), new Vector2(cw, ch), new Color(0.16f, 0.6f, 0.86f, 1f), 4);
                    offerBoxes.Add(box);
                    var rarity = o.Rarity == "epic" ? new Color(1f, 0.78f, 0.29f) : o.Rarity == "rare" ? new Color(0.6f, 0.9f, 1f) : Color.white;
                    Text(o.Title.ToUpperInvariant(), new Vector2(x, y + ch / 2f - 2.4f), 0.8f, cw - 0.8f, Color.white, height: 3.6f, wrap: true);
                    Text(o.Kind + " · " + o.Rarity.ToUpperInvariant(), new Vector2(x, y + ch / 2f - 4.8f), 0.5f, cw - 0.6f, rarity);
                    Text(o.Text, new Vector2(x, y - 2.2f), 0.6f, cw - 1.2f, Color.white, TextAlignmentOptions.Top, height: ch - 8f, wrap: true);
                }
                Text($"P{loser + 1}: LEFT / RIGHT TO CHOOSE, JUMP TO TAKE IT", new Vector2(0f, -ph / 2f + 1.6f), 0.5f, pw - 2f, new Color(0.85f, 0.95f, 1f));
                sel = 0;
            }
            else
            {
                float by = -ph / 2f + 2.6f, bw = pw * 0.22f;
                AddEnd($"CONTINUE  <size=60%>(FIRST TO {VersusState.Target + VersusRules.ContinueRounds})</size>", new Vector2(-bw * 1.15f, by), bw, () => VersusFlow.ContinueMatch());
                AddEnd("REMATCH", new Vector2(0f, by), bw, () => VersusFlow.Rematch());
                AddEnd("MENU", new Vector2(bw * 1.15f, by), bw, () => VersusFlow.ToMenu());
                endSel = 0;
            }
            Refresh();
        }

        private void AddEnd(string label, Vector2 at, float width, Action act)
        {
            endActions.Add(act);
            endButtons.Add(Button(label, at, new Vector2(width, 2.8f), () => Choose(act), 0.7f));
        }

        private void Choose(Action act)
        {
            if (done) return;
            done = true;
            act();
        }

        private void Refresh()
        {
            for (int i = 0; i < offerBoxes.Count; i++)
            {
                offerBoxes[i].color = i == sel ? new Color(0.45f, 0.82f, 1f, 1f) : new Color(0.16f, 0.6f, 0.86f, 1f);
                offerBoxes[i].transform.localScale = Vector3.one * (i == sel ? 1.06f : 1f);
            }
            for (int i = 0; i < endButtons.Count; i++) endButtons[i].transform.localScale = Vector3.one * (i == endSel ? 1.08f : 1f);
        }

        private void Update()
        {
            if (done) return;
            var setup = VersusState.Setup;
            if (setup == null) return;
            if (picker >= 0)
            {
                // only the loser's device chooses
                var s = VersusInput.Read(setup.Human(picker)?.Input);
                int dx = (s.Right ? 1 : 0) - (s.Left ? 1 : 0);
                if (dx != 0) { sel = Mathf.Clamp(sel + dx, 0, offers.Count - 1); Refresh(); }
                if (s.Confirm && offers.Count > 0)
                {
                    done = true;
                    VersusState.Take(picker, offers[sel]);
                    VersusFlow.NextRound();
                }
                return;
            }
            // match over: either player (or the mouse) chooses
            for (int side = 0; side < 2; side++)
            {
                var s = VersusInput.Read(setup.Human(side)?.Input);
                int dx = (s.Right ? 1 : 0) - (s.Left ? 1 : 0);
                if (dx != 0) { endSel = Mathf.Clamp(endSel + dx, 0, endButtons.Count - 1); Refresh(); }
                if (s.Confirm) { Choose(endActions[endSel]); return; }
            }
        }

        public static void Close()
        {
            if (open != null) Destroy(open.gameObject);
            open = null;
        }
    }

    // Round and match flow: the game's own match end is replaced by this in Versus.
    internal static class VersusFlow
    {
        // Called instead of the game's EndGame when a side reaches the round's points.
        public static IEnumerator RoundOver(GameManager gm, int winner)
        {
            gm.done = true;
            if (VersusDriver.Current != null) VersusDriver.Current.Frozen = true;
            VersusState.RoundWins[winner]++;
            VersusState.LastWinner = winner;
            bool over = VersusState.RoundWins[winner] >= VersusState.Target;
            Plugin.Log.LogInfo($"Versus: round {VersusState.Round} to P{winner + 1} ({gm.playerPoints}-{gm.opponentPoints}); rounds {VersusState.RoundWins[0]}-{VersusState.RoundWins[1]}{(over ? ", match over" : "")}");
            yield return new WaitForSeconds(1.3f);
            VersusOverlay.ShowRound(winner, over);
        }

        public static void NextRound()
        {
            VersusState.Round++;
            Runner.Instance.StartCoroutine(Reload(false));
        }

        public static void ContinueMatch()
        {
            VersusState.Continue();
            Runner.Instance.StartCoroutine(Reload(false));
        }

        public static void Rematch()
        {
            VersusState.Rematch();
            Runner.Instance.StartCoroutine(Reload(false));
        }

        public static void ToMenu()
        {
            VersusState.Leave();
            Runner.Instance.StartCoroutine(Reload(true));
        }

        private static IEnumerator Reload(bool toTitle)
        {
            var gm = GameManager.Instance;
            gm.transition.Play();
            yield return new WaitForSecondsRealtime(0.5f);
            VersusOverlay.Close();
            VersusOverlay.Notice(null);
            Time.timeScale = 1f;
            gm.ResetGame();
            PauseController.paused = false;
            if (toTitle)
            {
                GameManager.gameOver = true;
                GameManager.gameNumber = 0;
                TitleController.onTitle = true;
            }
            SceneManager.LoadScene("Game");
        }
    }
}

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
    internal abstract partial class OvertimePanel : MonoBehaviour
    {
        protected int layer, order;
        protected TextMeshPro fontSource;
        protected float W, H;
        protected Vector3 textScale = new Vector3(0.02f, 0.02f, 1f);
        private float baseOrtho;
        // the title menu's label (font, material, size, scale for its view), kept for scenes without one: there the kit
        // would copy whatever text it finds (the upgrade screen's animated dialogue) at that scene's camera size
        private static TMP_FontAsset kitFont;
        private static Material kitMaterial;
        private static float kitFontSize;
        private const float LayoutH = 36f;               // the title screen's settled view height: every panel is laid out for it
        private static Vector3 kitScale;
        private float fontFix = 1f;
        private float unit = 1f;                         // view height / LayoutH: the panel scales by it, so layouts fit any camera

        // the camera zooms (match point, kills): the panel keeps filling the same part of the screen
        protected virtual void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null && baseOrtho > 0f) transform.localScale = Vector3.one * unit * (cam.orthographicSize / baseOrtho);
            // everything the panel made goes on the menus' layer (drawn by the UI camera, without the game's grading)
            int n = transform.childCount;
            if (onUiCamera && (n != layeredChildren || Time.frameCount % 30 == 0)) { layeredChildren = n; UiCamera.SetLayer(gameObject); }
        }

        private bool onUiCamera;
        private int layeredChildren = -1;

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
            if (Plugin.DevKeys.Value) Plugin.Log.LogInfo($"[panel] {GetType().Name}: view {W:F1} x {H:F1}, label {fontSource.name} size {fontSource.fontSize:F1} scale {textScale.x:F4}");
            if (kitFont == null && fontSource.GetComponentInParent<TitleButton>() != null)
            {
                kitFont = fontSource.font; kitMaterial = fontSource.fontSharedMaterial; kitFontSize = fontSource.fontSize; kitScale = textScale;
            }
            if (kitFont != null)
            {
                textScale = kitScale;
                fontFix = kitFontSize / Mathf.Max(0.01f, fontSource.fontSize);
            }
            unit = H / LayoutH;
            H = LayoutH; W = LayoutH * cam.aspect;
            transform.SetParent(cam.transform, false);
            onUiCamera = UiCamera.Ensure(cam);
            if (onUiCamera) gameObject.layer = UiCamera.Layer;
            transform.localPosition = new Vector3(0f, 0f, 1f);
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one * unit;
            return true;
        }

        // A box: the hand-drawn panel shape (UiArt) when it's big enough for the drawn corners, else the plain rounded one.
        // The older menus' colours are mapped onto the new palette here. outline: a marker outline and a drop shadow (by
        // default for the "Panel" box; buttons get their own).
        protected SpriteRenderer Box(string name, Vector2 at, Vector2 size, Color color, int o, bool? outline = null)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.transform.localPosition = new Vector3(at.x, at.y, -0.001f * o);
            bool drawn = UiArt.Ready && Mathf.Min(size.x, size.y) >= UiArt.MinSize && size.x < W * 1.1f && color.a > 0.05f && name != "Focus";
            int v = Mathf.Abs(name.GetHashCode() ^ Mathf.RoundToInt(at.x * 7f) * 31 ^ Mathf.RoundToInt(at.y * 13f) * 17) % UiArt.Variants;
            sr.sprite = drawn ? UiArt.Fill(v) : ContinueMenu.Rounded();
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = size;
            UiArt.MakeUnlit(sr);
            sr.color = UiArt.Paint(color);
            sr.sortingLayerID = layer;
            sr.sortingOrder = order + o;
            if (drawn && (outline ?? name == "Panel")) Decorate(sr, 0.24f);
            return sr;
        }

        // a drawn box's marker outline (in front) and drop shadow (behind, down and right)
        protected static void Decorate(SpriteRenderer box, float shadow)
        {
            if (box.sprite == null || !box.sprite.name.StartsWith("panel_fill_")) return;
            int v = box.sprite.name[box.sprite.name.Length - 1] - '0';
            SpriteRenderer Part(string name, Sprite s, Color c, Vector3 at)
            {
                var r = new GameObject(name).AddComponent<SpriteRenderer>();
                r.transform.SetParent(box.transform, false);
                r.transform.localPosition = at;
                UiArt.MakeUnlit(r);
                r.sprite = s; r.drawMode = SpriteDrawMode.Sliced; r.size = box.size; r.color = UiArt.Paint(c);
                r.sortingLayerID = box.sortingLayerID; r.sortingOrder = box.sortingOrder;
                return r;
            }
            Part("Outline", UiArt.Line(v), UiArt.Ink, new Vector3(0f, 0f, -0.0004f));
            if (shadow > 0f) Part("Shadow", UiArt.Fill(v), UiArt.Shadow, new Vector3(shadow, -shadow, 0.0004f));
        }

        protected TextMeshPro Text(string s, Vector2 at, float size, float width, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center, int o = 8, float height = 3f, bool wrap = false)
        {
            var t = Instantiate(fontSource.gameObject, transform).GetComponent<TextMeshPro>();
            t.gameObject.SetActive(true);
            t.name = "Text";
            foreach (var mb in t.GetComponents<MonoBehaviour>()) if (!(mb is TMP_Text)) DestroyImmediate(mb);   // typewriters, wobbles
            if (kitFont != null) { t.font = kitFont; t.fontSharedMaterial = kitMaterial; }
            var lettering = UiArt.Lettering;
            if (lettering != null) { t.font = lettering; t.fontSharedMaterial = lettering.material; }
            var ls = textScale;
            t.transform.localScale = ls;
            t.transform.localRotation = Quaternion.identity;          // a tilted source label stays straight here
            t.transform.localPosition = new Vector3(at.x, at.y, -0.01f * o);
            t.text = s;
            t.alignment = align;
            t.autoSizeTextContainer = false;                     // the copied label fits its box to the text; wrapping needs a fixed box
            t.margin = Vector4.zero;                             // and it carries negative side margins that widen the box
            t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.enableAutoSizing = false;
            t.fontSize = fontSource.fontSize * size * fontFix * (lettering != null ? UiArt.LetteringScale : 1f);
            t.rectTransform.sizeDelta = new Vector2(width / ls.x, height / ls.y);
            t.color = color;
            var r = t.GetComponent<Renderer>();
            r.sortingLayerID = layer;
            r.sortingOrder = order + o;
            return t;
        }

        // ------------------------------------------------------------ text fields (lobby name, code, address, chat)

        internal class Field
        {
            public SpriteRenderer Box;
            public TextMeshPro Text;
            public string Value = "", Placeholder = "";
            public int Max = 24;
            public bool Upper;                                  // codes: capitals only
            public Action<string> OnSubmit;
            public OvertimePanel Owner;
        }

        private static Field focused;
        private static bool textHooked;
        private float backspaceAt;
        // a text field has the keyboard: the key sets' keys type instead of moving players
        public static bool Typing => focused != null && focused.Owner != null;

        protected Field TextField(Vector2 at, Vector2 size, string placeholder, int max, Action<string> onSubmit, string value = "", bool upper = false)
        {
            var normal = new Color(0.02f, 0.2f, 0.34f, 1f);
            var box = Box("Field", at, size, normal, 5);
            Decorate(box, 0f);
            var col = box.gameObject.AddComponent<BoxCollider2D>();
            col.size = size;
            var f = new Field { Box = box, Value = value ?? "", Placeholder = placeholder, Max = max, OnSubmit = onSubmit, Upper = upper, Owner = this };
            var w = box.gameObject.AddComponent<CreatorWidget>();
            w.Box = box; w.Normal = box.color; w.Hover = UiArt.Paint(new Color(0.05f, 0.3f, 0.48f, 1f)); w.OnClick = () => Focus(f);
            f.Text = Text("", at, 0.55f, size.x - 0.8f, Color.white, TextAlignmentOptions.Left, o: 9);
            ShowField(f);
            AddStop(box, () => Focus(f), f);
            if (!textHooked && UnityEngine.InputSystem.Keyboard.current != null)
            {
                textHooked = true;
                UnityEngine.InputSystem.Keyboard.current.onTextInput += OnChar;
            }
            return f;
        }

        protected static void Focus(Field f)
        {
            var old = focused;
            focused = f;
            if (old != null && old != f) ShowField(old);
            if (f != null) ShowField(f);
        }

        protected static bool IsFocused(Field f) => f != null && focused == f;

        private static void OnChar(char c)
        {
            var f = focused;
            if (f == null || f.Owner == null || char.IsControl(c) || f.Value.Length >= f.Max) return;
            f.Value += f.Upper ? char.ToUpperInvariant(c) : c;
            ShowField(f);
        }

        protected static void ShowField(Field f)
        {
            if (f?.Text == null) return;
            bool on = focused == f;
            string shown = f.Value.Replace("<", "‹").Replace(">", "›");
            f.Text.text = shown.Length == 0 && !on ? $"<color=#7FA9C4>{f.Placeholder}</color>" : shown + (on ? "<color=#FFD95A>|</color>" : "");
            if (f.Box != null) f.Box.color = UiArt.Paint(on ? new Color(0.08f, 0.36f, 0.56f, 1f) : new Color(0.02f, 0.2f, 0.34f, 1f));
        }

        public static void SetField(Field f, string value) { if (f == null) return; f.Value = value ?? ""; ShowField(f); }

        // the focused field's editing keys (called by the panel that owns it); true while a field had the keyboard this
        // frame, so the key that ends typing (Enter, Esc) isn't also read as a game key
        protected bool FieldKeys()
        {
            var f = focused;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (f == null || f.Owner != this || kb == null) return false;
            if (kb.escapeKey.wasPressedThisFrame) { Focus(null); return true; }
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) { var cb = f.OnSubmit; Focus(null); cb?.Invoke(f.Value); return true; }
            if (kb.backspaceKey.wasPressedThisFrame) backspaceAt = Time.unscaledTime + 0.4f;
            if (f.Value.Length > 0 && (kb.backspaceKey.wasPressedThisFrame || (kb.backspaceKey.isPressed && Time.unscaledTime > backspaceAt)))
            {
                f.Value = f.Value.Substring(0, f.Value.Length - 1);
                if (!kb.backspaceKey.wasPressedThisFrame) backspaceAt = Time.unscaledTime + 0.04f;
                ShowField(f);
            }
            return true;
        }

        protected virtual void OnDestroy()
        {
            if (focused != null && focused.Owner == this) focused = null;
            ReleaseTitle();
        }

        // ------------------------------------------------------------ the title menu behind a panel

        // one hold for all panels: a panel that opens another and closes in the same frame (its OnDestroy runs at the end of
        // the frame, after the new one held the menu) mustn't hand the title menu back while the new one is up
        private static int titleHolds;
        private static ButtonGroup heldGroup;
        private static readonly List<Collider2D> heldColliders = new List<Collider2D>();
        private bool holdingTitle;

        // the title menu's buttons stop taking clicks and keys until this panel closes
        protected void HoldTitle()
        {
            if (holdingTitle) return;
            holdingTitle = true;
            if (titleHolds++ > 0 && heldGroup != null) return;
            var classic = VersusMenu.ClassicButton;
            heldGroup = classic != null ? classic.GetComponentInParent<ButtonGroup>(true) : null;
            if (heldGroup == null) return;
            heldGroup.enabled = false;
            foreach (var b in heldGroup.buttons.Where(b => b != null))
                foreach (var c in b.GetComponents<Collider2D>().Where(c => c.enabled)) { c.enabled = false; heldColliders.Add(c); }
        }

        protected void ReleaseTitle()
        {
            if (!holdingTitle) return;
            holdingTitle = false;
            if (--titleHolds > 0) return;
            titleHolds = 0;
            foreach (var c in heldColliders) if (c != null) c.enabled = true;
            heldColliders.Clear();
            if (heldGroup == null) return;
            heldGroup.enabled = true;
            foreach (var b in heldGroup.buttons.Where(b => b != null)) { b.interactable = true; b.wasClicked = false; }
            heldGroup = null;
        }

        // an on/off switch: "LABEL  ON"
        protected SpriteRenderer Toggle(string label, Vector2 at, Vector2 size, Func<bool> get, Action<bool> set, float textSize = 0.55f)
        {
            TextMeshPro t = null;
            SpriteRenderer box = null;
            void Show() { bool v = get(); if (t != null) t.text = $"{label}  <color={(v ? "#7CFF8A" : "#FF8A7C")}>{(v ? "ON" : "OFF")}</color>"; }
            box = Button("", at, size, () => { set(!get()); Show(); }, textSize);   // Button() already made it a stop
            t = Text("", at, textSize, size.x, Color.white, o: 9);
            Show();
            return box;
        }

        // a button in other colours (list entries whose text has its own colours, the chosen tab); hover keeps them
        public static Color ListColor => UiArt.Blue;
        protected static SpriteRenderer Shade(SpriteRenderer b, Color normal, Color? hover = null)
        {
            var raw = UiArt.Remap(normal);
            b.color = UiArt.Paint(raw);
            var w = b.GetComponent<CreatorWidget>();
            if (w != null) { w.Normal = b.color; w.Hover = UiArt.Paint(hover ?? Color.Lerp(raw, Color.white, 0.25f)); }
            return b;
        }

        protected SpriteRenderer Button(string label, Vector2 at, Vector2 size, Action act, float textSize = 0.8f)
        {
            var normal = UiArt.Blue;
            var sr = Box("Button " + label, at, size, normal, 5, false);
            Decorate(sr, 0.14f);
            var col = sr.gameObject.AddComponent<BoxCollider2D>();
            col.size = size;
            var w = sr.gameObject.AddComponent<CreatorWidget>();
            w.Box = sr; w.Normal = sr.color; w.Hover = UiArt.Paint(UiArt.BlueHover); w.OnClick = act;
            Text(label, at, textSize, size.x, Color.white);
            AddStop(sr, act);
            return sr;
        }
    }

    // What the between-rounds / match-end screen shows. The host fills it from the match; an online client gets it sent.
    internal class RoundScreen
    {
        public bool MatchOver;
        public int Winner;
        public string Head = "", Score = "", Sub = "", PickHead = "", PickHint = "", ContinueLabel = "", RematchLabel = "REMATCH";
        public readonly string[] SideTitle = new string[2];
        public readonly List<string>[] Builds = { new List<string>(), new List<string>() };
        public readonly List<(string title, string kind, string rarity, string text)> Offers = new List<(string, string, string, string)>();

        public static RoundScreen From(int winner, bool matchOver, List<VersusOffer> offers, VersusSlot picker)
        {
            var setup = VersusState.Setup;
            int loser = 1 - winner;
            int[] w = VersusState.RoundWins;
            string Name(int side) => setup.SideLabel(side);
            var s = new RoundScreen { MatchOver = matchOver, Winner = winner };
            s.Head = matchOver ? $"{Name(winner)} {(setup.Humans(winner).Count > 1 ? "WIN" : "WINS")} THE MATCH!" : $"ROUND {VersusState.Round} TO {Name(winner)}";
            s.Score = $"{Name(0)}  {w[0]} - {w[1]}  {Name(1)}";
            s.Sub = matchOver ? "" : $"FIRST TO {VersusState.Target} ROUNDS";
            for (int side = 0; side < 2; side++)
            {
                s.SideTitle[side] = $"{Name(side)} - {VersusState.TeamName(side, setup.Team[side]).ToUpperInvariant()}";
                s.Builds[side].AddRange(VersusState.Build(side));
            }
            if (!matchOver)
            {
                string team = setup.Humans(loser).Count > 1 ? $" FOR {Name(loser)}" : "";
                s.PickHead = picker == null ? $"{Name(loser)} (AI) PICKS AN UPGRADE" : $"P{picker.Number} PICKS AN UPGRADE{team}  <size=70%>({picker.Input?.Label})</size>";
                s.PickHint = picker == null ? "THE AI IS CHOOSING..." : $"P{picker.Number}: LEFT / RIGHT TO CHOOSE, JUMP TO TAKE IT";
                foreach (var o in offers) s.Offers.Add((o.Title, o.Kind, o.Rarity, o.Text));
            }
            s.ContinueLabel = $"CONTINUE  <size=60%>(FIRST TO {VersusState.Target + VersusRules.ContinueRounds})</size>";
            return s;
        }

        public void Write(Online.NetWriter w)
        {
            w.B(MatchOver).U8(Winner).S(Head).S(Score).S(Sub).S(PickHead).S(PickHint).S(ContinueLabel).S(RematchLabel).S(SideTitle[0]).S(SideTitle[1]);
            for (int side = 0; side < 2; side++) { w.I(Builds[side].Count); foreach (var b in Builds[side]) w.S(b); }
            w.I(Offers.Count);
            foreach (var o in Offers) w.S(o.title).S(o.kind).S(o.rarity).S(o.text);
        }

        public static RoundScreen Read(Online.NetReader r)
        {
            var s = new RoundScreen { MatchOver = r.B(), Winner = r.U8(), Head = r.S(), Score = r.S(), Sub = r.S(), PickHead = r.S(), PickHint = r.S(), ContinueLabel = r.S(), RematchLabel = r.S() };
            s.SideTitle[0] = r.S(); s.SideTitle[1] = r.S();
            for (int side = 0; side < 2; side++) { int n = r.I(); for (int i = 0; i < n; i++) s.Builds[side].Add(r.S()); }
            int k = r.I();
            for (int i = 0; i < k; i++) s.Offers.Add((r.S(), r.S(), r.S(), r.S()));
            return s;
        }
    }

    // Between rounds: the round score, both builds, and the loser's pick (only the loser's device can choose).
    // End of match: both builds and CONTINUE / REMATCH / MENU. Online, the host runs it and the client shows a copy.
    internal class VersusOverlay : OvertimePanel
    {
        private static VersusOverlay open;
        private static GameObject notice;
        private int picker = -1;                      // side choosing an upgrade, or -1
        private VersusSlot pickerSlot;                // the player choosing for that side
        private List<VersusOffer> offers;
        private bool mirror;                          // an online client's copy: no input, the host moves the highlight
        private readonly List<SpriteRenderer> offerBoxes = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> endButtons = new List<SpriteRenderer>();
        private readonly List<Action> endActions = new List<Action>();
        private int sel, endSel;
        private bool done;
        private float shownAt;
        private Action afterPick;                     // the draft start: what comes after this side's pick (else the next round)
        internal static int picksLeft;                // the lobby rules' upgrades per round still to take this round
        private int winner;
        private float autoNextAt = -1f;               // no upgrades this round (rules): the next round starts by itself

        private void Awake() => shownAt = Time.unscaledTime;

        public static void ShowRound(int winner, bool matchOver)
        {
            if (open != null) Destroy(open.gameObject);
            var go = new GameObject("Overtime Versus Overlay");
            open = go.AddComponent<VersusOverlay>();
            if (!open.InitKit(300)) { Plugin.Log.LogWarning("Versus overlay: no camera/font; skipping to the next round"); Destroy(go); VersusFlow.NextRound(); return; }
            if (matchOver && Party.Active) { Destroy(go); open = null; PartyFlow.MatchOver(winner); return; }   // a party cup: the bracket, then its next match
            bool coop = VersusState.Coop;
            int loser = coop ? 0 : 1 - winner;                 // co-op: the players pick after every win
            open.winner = winner;
            if (!matchOver && picksLeft <= 0) picksLeft = coop ? Mathf.Max(1, VersusState.Setup.Humans(0).Count) : LobbyRules.Current.Picks;
            if (!matchOver)
            {
                open.picker = loser;
                open.offers = VersusState.Offer(loser);
                open.pickerSlot = VersusState.Picker(loser);
                if (open.offers.Count == 0)
                {
                    open.picker = -1; picksLeft = 0;
                    open.autoNextAt = Time.unscaledTime + 3.5f;
                }
                Plugin.Log.LogInfo($"Versus: P{open.pickerSlot?.Number} ({VersusState.Setup.SideLabel(loser)}) offered " + string.Join(" | ", open.offers.Select((o, i) => $"{i}: {o.Title} ({o.Kind})")));
            }
            var data = RoundScreen.From(winner, matchOver, open.offers, open.pickerSlot);
            if (!matchOver && open.picker < 0) { data.PickHead = "NO UPGRADES THIS MATCH  <size=70%>(LOBBY RULES)</size>"; data.PickHint = "THE NEXT ROUND STARTS IN A MOMENT"; }
            else if (!matchOver && !coop && LobbyRules.Current.Picks > 1) data.PickHead += $"  <size=70%>· PICK {LobbyRules.Current.Picks - picksLeft + 1} OF {LobbyRules.Current.Picks}</size>";
            if (coop) CoopScreen(data, matchOver, open.pickerSlot);
            open.Build(data);
            Online.OnlineFlow.SendScreen(data);
        }

        // co-op Infinite's texts: the run so far, whose pick it is, the opponent's perks
        private static void CoopScreen(RoundScreen d, bool over, VersusSlot picker)
        {
            int wins = VersusState.RoundWins[0];
            d.Head = over ? $"RUN OVER  ·  {wins} {(wins == 1 ? "WIN" : "WINS")}" : $"MATCH {VersusState.Round} WON!";
            d.Score = over ? $"BEST CO-OP RUN: {Records.Data.coopInfiniteBest} WINS" : $"{wins} {(wins == 1 ? "WIN" : "WINS")} IN A ROW";
            int next = VersusState.Round + 1, slot = (next - 1) % 8 + 1;
            d.Sub = over ? "" : $"NEXT: MATCH {next}" + (slot == 3 || slot == 6 || slot == 8 ? " (BOSS)" : slot == 7 ? " (COMBO)" : "");
            if (!over && picker != null) d.PickHead = $"P{picker.Number} PICKS AN UPGRADE  <size=70%>({picker.Input?.Label})</size>";
            d.SideTitle[1] = "THE OPPONENT";
            d.Builds[1].Clear();
            d.Builds[1].AddRange(OpponentScaling.LastGiven.Select(t => t is DataPerk p ? p.Def.Title : PerkRegistry.VanillaTitle(t)));
            d.ContinueLabel = "";
            d.RematchLabel = "TRY AGAIN";
        }

        // the draft start (before round 1): a side's picker takes one of three drafted teams, then `then` runs
        public static void ShowDraft(int side, int seed, Action then)
        {
            if (open != null) Destroy(open.gameObject);
            var go = new GameObject("Overtime Versus Draft");
            open = go.AddComponent<VersusOverlay>();
            if (!open.InitKit(300)) { Destroy(go); then(); return; }
            open.picker = side;
            open.offers = VersusState.DraftOffer(side, seed);
            open.pickerSlot = VersusState.Picker(side);
            open.afterPick = then;
            var setup = VersusState.Setup;
            if (open.pickerSlot == null) { VersusState.Take(side, open.offers[0]); Destroy(go); then(); return; }   // an all-AI side drafts the first team
            var data = RoundScreen.From(1 - side, false, open.offers, open.pickerSlot);
            data.Head = $"DRAFT  ·  {setup.SideLabel(side)} PICKS A TEAM";
            data.Score = $"{setup.SideLabel(0)}  VS  {setup.SideLabel(1)}";
            data.Sub = "EACH TEAM COMES WITH THREE ABILITIES AND TWO STAT CARDS, DEALT TO BE ABOUT EQUALLY STRONG";
            data.PickHead = $"P{open.pickerSlot.Number} DRAFTS FOR {setup.SideLabel(side)}  <size=70%>({open.pickerSlot.Input?.Label})</size>";
            Plugin.Log.LogInfo($"Versus draft: P{open.pickerSlot.Number} ({setup.SideLabel(side)}) offered " + string.Join(" | ", open.offers.Select((o, i) => $"{i}: {o.Title}")));
            open.Build(data);
            Online.OnlineFlow.SendScreen(data);
        }

        // an online client: the host's screen
        public static void ShowMirror(RoundScreen data)
        {
            if (open != null) Destroy(open.gameObject);
            var go = new GameObject("Overtime Versus Overlay (online)");
            open = go.AddComponent<VersusOverlay>();
            open.mirror = true;
            MirrorSerial++;
            if (!open.InitKit(300)) { Destroy(go); open = null; return; }
            open.Build(data);
        }

        public static bool IsMirror => open != null && open.mirror;
        public static int MirrorSerial;                   // counts the host's screens shown here (a new one may follow at once)
        public static bool Picking => open != null && !open.mirror && open.picker >= 0;
        public static bool MirrorMatchOver => IsMirror && open.endButtons.Count > 0;

        public static void MirrorSelection(int sel, int endSel)
        {
            if (open == null || !open.mirror) return;
            open.sel = sel; open.endSel = endSel;
            open.Refresh();
        }

        public static void Notice(string text)
        {
            if (notice != null) Destroy(notice);
            Online.OnlineFlow.SendNotice(text);
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

        private void Build(RoundScreen d)
        {
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.08f, 0.16f, 0.62f), 0);
            float pw = W * 0.88f, ph = H * 0.86f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            float top = ph / 2f;
            Text(d.Head, new Vector2(0f, top - 2f), 1.3f, pw - 2f, Color.white);
            Text(d.Score, new Vector2(0f, top - 4.6f), 1.6f, pw - 2f, new Color(1f, 0.85f, 0.35f));
            Text(d.MatchOver && StreakPatches.VersusLine != null && !VersusState.Coop ? StreakPatches.VersusLine : d.Sub, new Vector2(0f, top - 6.6f), 0.6f, pw - 2f,
                 d.MatchOver ? new Color(1f, 0.85f, 0.35f) : new Color(0.85f, 0.95f, 1f));

            // both builds, left and right
            for (int side = 0; side < 2; side++)
            {
                float x = (side == 0 ? -1f : 1f) * pw * 0.36f;
                Text(d.SideTitle[side], new Vector2(x, top - 8.6f), 0.7f, pw * 0.26f, Color.white);
                var build = d.Builds[side];
                string list = build.Count == 0 ? "NO UPGRADES YET" : string.Join("\n", build.Take(12).Select(b => b.ToUpperInvariant())) + (build.Count > 12 ? $"\n+{build.Count - 12} MORE" : "");
                Text(list, new Vector2(x, top - 8.6f - 1.4f - 7f), 0.68f, pw * 0.26f, new Color(0.85f, 0.95f, 1f), TextAlignmentOptions.Top, height: 14f, wrap: true);
            }

            if (!d.MatchOver)
            {
                Text(d.PickHead, new Vector2(0f, top - 8.6f), 0.8f, pw * 0.42f, Color.white, height: 3f, wrap: true);
                float cw = Mathf.Min(pw * 0.15f, 9f), ch = ph * 0.45f, gap = cw * 0.12f;
                for (int i = 0; i < d.Offers.Count; i++)
                {
                    float x = (i - (d.Offers.Count - 1) / 2f) * (cw + gap);
                    float y = -ph * 0.08f;
                    var o = d.Offers[i];
                    var box = Box("Offer", new Vector2(x, y), new Vector2(cw, ch), new Color(0.16f, 0.6f, 0.86f, 1f), 4);
                    offerBoxes.Add(box);
                    var rarity = o.rarity == "epic" ? new Color(1f, 0.78f, 0.29f) : o.rarity == "rare" ? new Color(0.6f, 0.9f, 1f) : Color.white;
                    Text(o.title.ToUpperInvariant(), new Vector2(x, y + ch / 2f - 2.4f), 0.8f, cw - 0.8f, Color.white, height: 3.6f, wrap: true);
                    Text(o.kind + " · " + o.rarity.ToUpperInvariant(), new Vector2(x, y + ch / 2f - 4.8f), 0.42f, cw - 0.6f, rarity, height: 1.8f, wrap: true);
                    Text(o.text, new Vector2(x, y - 2.2f), 0.6f, cw - 1.2f, Color.white, TextAlignmentOptions.Top, height: ch - 8f, wrap: true);
                }
                Text(d.PickHint, new Vector2(0f, -ph / 2f + 1.6f), 0.5f, pw - 2f, new Color(0.85f, 0.95f, 1f));
                sel = 0;
            }
            else
            {
                float by = -ph / 2f + 2.6f, bw = pw * 0.22f;
                if (d.ContinueLabel.Length > 0) AddEnd(d.ContinueLabel, new Vector2(-bw * 1.15f, by), bw, () => VersusFlow.ContinueMatch());
                AddEnd(d.RematchLabel, new Vector2(d.ContinueLabel.Length > 0 ? 0f : -bw * 0.6f, by), bw, () => VersusFlow.Rematch());
                AddEnd(Online.Net.Online ? "LOBBY" : "MENU", new Vector2(d.ContinueLabel.Length > 0 ? bw * 1.15f : bw * 0.6f, by), bw, () => { if (Online.Net.IsHost) Online.OnlineFlow.BackToLobby(); else VersusFlow.ToMenu(); });
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
            if (done || mirror) return;
            done = true;
            act();
        }

        private void Refresh()
        {
            for (int i = 0; i < offerBoxes.Count; i++)
            {
                offerBoxes[i].color = UiArt.Paint(i == sel ? new Color(0.45f, 0.82f, 1f, 1f) : new Color(0.16f, 0.6f, 0.86f, 1f));
                offerBoxes[i].transform.localScale = Vector3.one * (i == sel ? 1.06f : 1f);
            }
            for (int i = 0; i < endButtons.Count; i++) endButtons[i].transform.localScale = Vector3.one * (i == endSel ? 1.08f : 1f);
            if (!mirror) Online.OnlineFlow.SendSelection(sel, endSel);
        }

        private void Update()
        {
            if (done || mirror) return;
            var setup = VersusState.Setup;
            if (setup == null) return;
            if (autoNextAt > 0f && Time.unscaledTime > autoNextAt) { done = true; VersusFlow.NextRound(); return; }
            // the AI's side (a party cup) picks for itself after a moment
            if (picker >= 0 && pickerSlot == null && offers.Count > 0)
            {
                if (Time.unscaledTime - shownAt < 2f) return;
                done = true;
                VersusState.Take(picker, offers[UnityEngine.Random.Range(0, offers.Count)]);
                if (--picksLeft > 0) ShowRound(winner, false); else VersusFlow.NextRound();
                return;
            }
            if (picker >= 0)
            {
                // only the losing side's picker chooses (a pair takes turns, round by round)
                var s = VersusInput.Read(pickerSlot?.Input);
                // development autopilot: a player on this machine takes the second offer after a moment
                if (VersusDriver.Autopilot && pickerSlot?.Input?.Kind != InputKind.Remote && Time.unscaledTime - shownAt > 2.5f)
                {
                    if (sel < Mathf.Min(1, offers.Count - 1)) s.Right = true; else s.Confirm = true;
                }
                int dx = (s.Right ? 1 : 0) - (s.Left ? 1 : 0);
                if (dx != 0) { sel = Mathf.Clamp(sel + dx, 0, offers.Count - 1); Refresh(); }
                if (s.Confirm && offers.Count > 0)
                {
                    done = true;
                    VersusState.Take(picker, offers[sel]);
                    if (afterPick != null) { var next = afterPick; afterPick = null; Destroy(gameObject); next(); }
                    else if (--picksLeft > 0) ShowRound(winner, false);          // the rules give the side another pick
                    else VersusFlow.NextRound();
                }
                return;
            }
            // match over: any player (or the mouse) chooses
            foreach (var h in setup.AllHumans)
            {
                var s = VersusInput.Read(h.Input);
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
            bool over = VersusState.Coop ? winner == 1 : VersusState.RoundWins[winner] >= VersusState.Target;
            if (VersusState.Coop) StreakPatches.RecordCoop(VersusState.Round, winner == 0, VersusState.RoundWins[0]);
            Plugin.Log.LogInfo($"Versus: round {VersusState.Round} to {VersusState.Setup.SideLabel(winner)} ({gm.playerPoints}-{gm.opponentPoints}); rounds {VersusState.RoundWins[0]}-{VersusState.RoundWins[1]}{(over ? ", match over" : "")}");
            if (over && !VersusState.Coop) StreakPatches.RecordVersus(winner);
            yield return new WaitForSeconds(1.3f);
            VersusOverlay.ShowRound(winner, over);
        }

        // a new match from inside a match scene (a party cup's next match)
        public static void StartNewMatch() => Runner.Instance.StartCoroutine(Reload(false));

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

        // an online client: the host started the next round
        public static void ReloadRound() => Runner.Instance.StartCoroutine(Reload(false));

        private static IEnumerator Reload(bool toTitle)
        {
            var gm = GameManager.Instance;
            Online.MatchSync.InMatch = false;
            if (!toTitle && VersusState.Coop && !Online.Net.IsClient) VersusState.PickCoopOpponent();
            if (!toTitle && Online.Net.IsHost) Online.OnlineFlow.SendLoad(false);
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

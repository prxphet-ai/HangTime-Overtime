using System;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Engine;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HangtimeOvertime.Patches
{
    // A clickable box for the creator (mouse); keyboard/gamepad go through CreatorMenu.Update.
    internal class CreatorWidget : MonoBehaviour
    {
        public Action OnClick;
        public SpriteRenderer Box;
        public Color Normal, Hover;
        private void OnMouseEnter() { if (Box != null) Box.color = Hover; }
        private void OnMouseExit() { if (Box != null) Box.color = Normal; }
        private void OnMouseDown() => OnClick?.Invoke();
    }

    // CUSTOMIZE (title screen): the player's look, laid out like the tournaments screen: a stage with a live preview of the
    // player on the left, one row per option on the right (left / right or the arrows change it; a colour swatch where it has
    // one), RANDOMIZE / RESET and a gold DONE. The title-screen player wears the look as it changes.
    internal class CreatorMenu : OvertimePanel
    {
        private static readonly AccessTools.FieldRef<ButtonController, OnClick> onClickOf = AccessTools.FieldRefAccess<ButtonController, OnClick>("onClick");

        private static CreatorMenu open;
        public static bool IsOpen => open != null;
        private PlayerLook work;
        private Transform preview, titleRig;
        private readonly List<(string label, string field)> rows = new List<(string, string)>
        {
            ("HAIR STYLE", "hairStyle"), ("HAIR COLOR", "hairColor"), ("SKIN TONE", "skin"), ("JERSEY", "jersey"), ("SHORTS & NUMBER", "trim"),
            ("NUMBER", "number"), ("BUILD", "build"), ("HEADBAND", "headband"), ("WRISTBANDS", "wristbands"), ("KNEE PADS", "kneePads"), ("SHOES", "shoes"),
        };

        // ------------------------------------------------------------ the title-screen button

        public static void AddTitleButton(GameObject classic)
        {
            if (classic == null) return;
            var group = classic.GetComponentInParent<ButtonGroup>(true);
            if (group == null || group.buttons.Any(b => b != null && b.name == "Customize Button")) return;
            var player = FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(Look.IsPlayerOne);
            var b = Instantiate(classic, classic.transform.parent);
            b.name = "Customize Button";
            foreach (var m in b.GetComponents<MonoBehaviour>().Where(m => m is TitleButton || m is InfiniteButtonMarker)) DestroyImmediate(m);
            var click = b.AddComponent<PopupClick>();
            click.Action = () => Open(classic);
            var ctrl = b.GetComponent<ButtonController>();
            onClickOf(ctrl) = click;
            ctrl.group = group;
            MenuPatches.SetButtonLabels(b, "CUSTOMIZE", "YOUR PLAYER");
            b.transform.localScale = classic.transform.localScale * 0.7f;
            Traverse.Create(ctrl).Field("originalScale").SetValue(b.transform.localScale.x);   // its hover grows from the smaller size
            var at = player != null ? player.transform.position + new Vector3(0.5f, 9.5f, 0f) : classic.transform.position + new Vector3(-12f, 0f, 0f);
            b.transform.position = new Vector3(at.x, at.y, classic.transform.position.z);
            int quit = group.buttons.FindIndex(x => x != null && x.GetComponent<QuitButton>());
            group.buttons.Insert(quit < 0 ? group.buttons.Count : quit, ctrl);
        }

        // ------------------------------------------------------------ open / close

        private static void Open(GameObject template)
        {
            if (open != null) return;
            var go = new GameObject("Overtime Creator");
            open = go.AddComponent<CreatorMenu>();
            if (!open.InitKit(200)) { Destroy(go); open = null; return; }
            open.HoldTitle();
            open.work = JsonUtility.FromJson<PlayerLook>(JsonUtility.ToJson(Look.Current));
            var player = FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(Look.IsPlayerOne);
            if (player != null) open.titleRig = Look.RigOf(player);
            open.Build();
        }

        private void Close(bool keep)
        {
            if (keep) { Look.Current = work; Look.Save(); }
            if (titleRig != null) Look.Apply(titleRig, Look.Current);
            Destroy(gameObject);
        }

        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }

        // ------------------------------------------------------------ layout

        private void Build()
        {
            int keep = focus;
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops();
            preview = null;
            Frame("CUSTOMIZE", null);

            // the stage and the live preview
            Rect("Poster", 80, 170, 620, 800, new Color(0.03f, 0.11f, 0.2f, 1f), 2);
            Rect("Stage", 110, 200, 590, 720, new Color(0.62f, 0.86f, 0.97f, 1f), 3);
            Rect("Floor", 110, 640, 590, 720, new Color(1f, 0.6f, 0.25f, 1f), 4);
            TC("YOUR PLAYER", 350, 760, 30, UiArt.Gold, 400);
            if (titleRig != null) MakePreview();

            // option rows
            float y0 = 170f, rh = 57f;
            for (int i = 0; i < rows.Count; i++)
            {
                int row = i;
                float y = y0 + i * rh, cy = y + rh / 2f - 2f;
                var b = Shade(Btn("", 650, y, 1530, y + rh - 6f, () => Step(row, 1)), i % 2 == 0 ? new Color(0.094f, 0.275f, 0.46f, 1f) : new Color(0.075f, 0.23f, 0.4f, 1f));
                var stop = stops.Find(x => x.Box == b);
                if (stop != null) stop.Adjust = d => Step(row, d);
                int v = Get(rows[i].field);
                TL(rows[i].label, 680, cy, 28, UiArt.Ink, 380);
                var hex = SwatchHex(rows[i].field, v);
                if (hex != null) Rect("Swatch", 1080, cy - 17, 1124, cy + 17, Vfx.Hex(hex), 7, true);
                foreach (var (x, d) in new[] { (1160f, -1), (1490f, 1) })
                {
                    var arrow = Btn(d < 0 ? "<" : ">", x - 22, cy - 20, x + 22, cy + 20, () => Step(row, d), 24);
                    stops.RemoveAll(st => st.Box == arrow);
                }
                TC(ValueText(rows[i].field, v).ToUpperInvariant(), 1325, cy, 28, UiArt.Gold, 280);
            }

            // footer
            IconBtn("star", "RANDOMIZE", 650, 846, 950, 892, Randomize);
            IconBtn("refresh", "RESET", 970, 846, 1200, 892, ResetLook);
            GoldSign("DONE", 1300, 838, 1540, 898, () => Close(true), 36, "check");
            PadHint("UP / DOWN: CHOOSE   LEFT / RIGHT: CHANGE   B: DONE", P(330, 870), 520 / 25f * KX);
            navKeys = true;
            focus = keep >= 0 && keep < stops.Count ? keep : 0;
            if (preview != null) Look.Apply(preview, work);
            if (titleRig != null) Look.Apply(titleRig, work);
        }

        // a copy of the title-screen player, larger, standing on the stage
        private void MakePreview()
        {
            Look.Apply(titleRig, new PlayerLook());          // copied as the game draws it, so "Original" means the game's look
            titleRig.GetComponent<LookKeeper>()?.SnapBuild();
            var copy = Instantiate(titleRig.gameObject, transform);
            foreach (var lk in copy.GetComponentsInChildren<LookKeeper>(true)) DestroyImmediate(lk);
            foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m is PlayerController || m is Rigidbody2D)) mb.enabled = false;
            foreach (var rb in copy.GetComponentsInChildren<Rigidbody2D>(true)) rb.simulated = false;
            foreach (var c in copy.GetComponentsInChildren<Collider2D>(true)) c.enabled = false;
            preview = copy.transform;
            preview.localRotation = Quaternion.identity;
            preview.localScale = titleRig.lossyScale * 2.4f / Mathf.Max(0.0001f, transform.lossyScale.x);
            var b = Bounds(preview);
            var want = transform.TransformPoint(P(350, 500));
            preview.position += new Vector3(want.x - b.center.x, want.y - b.center.y, 0f);
            preview.localPosition = new Vector3(preview.localPosition.x, preview.localPosition.y, -0.06f);
            foreach (var r in preview.GetComponentsInChildren<Renderer>(true)) { r.sortingLayerID = layer; r.sortingOrder = order + 10 + r.sortingOrder; }
            UiCamera.SetLayer(copy);
        }

        private static Bounds Bounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.enabled).ToArray();
            if (rs.Length == 0) return new Bounds(t.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------ values

        private int Get(string f) => (int)typeof(PlayerLook).GetField(f).GetValue(work);
        private void Set(string f, int v) => typeof(PlayerLook).GetField(f).SetValue(work, v);

        private void Step(int row, int dir)
        {
            string f = rows[row].field;
            int n = Look.Count(f);
            Set(f, ((Get(f) + dir) % n + n) % n);
            Build();
        }

        private void Randomize() { work = Look.Randomized(); Build(); }
        private void ResetLook() { work = new PlayerLook(); Build(); }

        private string ValueText(string f, int v)
        {
            switch (f)
            {
                case "hairStyle": return Look.HairStyles[v].name;
                case "hairColor": return Look.HairColors[v].name;
                case "skin": return Look.SkinTones[v].name;
                case "build": return Look.Builds[v].name;
                case "number": return v.ToString();
                case "headband": case "wristbands": case "kneePads": return Look.AccessoryName(v);
                default: return Look.UniformColors[v].name;
            }
        }

        private string SwatchHex(string f, int v)
        {
            switch (f)
            {
                case "hairColor": return Look.HairColors[v].hex;
                case "skin": return v == 0 ? "F4C8A4" : Vfx.ToHex(Vfx.Hex("F4C8A4") * Vfx.Hex(Look.SkinTones[v].hex));
                case "jersey": case "trim": case "shoes": case "headband": case "wristbands": case "kneePads": return v == 0 ? null : Look.UniformColors[v].hex;
                default: return null;
            }
        }

        private void Update()
        {
            if (NavUpdate()) return;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Close(true);
        }

        protected override void OnNavBack() => Close(true);
    }
}

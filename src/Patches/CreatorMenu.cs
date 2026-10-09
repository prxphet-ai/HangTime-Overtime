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

    // Character creator: title-screen CUSTOMIZE button -> a panel with a live preview of the player and one row per option.
    internal class CreatorMenu : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<ButtonController, OnClick> onClickOf = AccessTools.FieldRefAccess<ButtonController, OnClick>("onClick");

        private static CreatorMenu open;
        private PlayerLook work;
        private Transform preview, titleRig;
        private ButtonGroup titleGroup;
        private readonly List<Collider2D> disabled = new List<Collider2D>();
        private readonly List<(string label, string field)> rows = new List<(string, string)>
        {
            ("HAIR STYLE", "hairStyle"), ("HAIR COLOR", "hairColor"), ("SKIN TONE", "skin"), ("JERSEY", "jersey"), ("SHORTS & NUMBER", "trim"),
            ("NUMBER", "number"), ("BUILD", "build"), ("HEADBAND", "headband"), ("WRISTBANDS", "wristbands"), ("KNEE PADS", "kneePads"), ("SHOES", "shoes"),
        };
        private readonly List<TextMeshPro> values = new List<TextMeshPro>();
        private readonly List<SpriteRenderer> swatches = new List<SpriteRenderer>(), rowBoxes = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> footer = new List<SpriteRenderer>();
        private int selected, footerSel;
        private float stickTimer;
        private TextMeshPro fontSource;
        private int layer, order;

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
            open.Build(template);
        }

        private void Close(bool keep)
        {
            if (keep) { Look.Current = work; Look.Save(); }
            if (titleRig != null) Look.Apply(titleRig, Look.Current);
            foreach (var c in disabled) if (c != null) c.enabled = true;
            if (titleGroup != null)
            {
                titleGroup.enabled = true;
                foreach (var b in titleGroup.buttons.Where(b => b != null)) { b.interactable = true; b.wasClicked = false; }
            }
            open = null;
            Destroy(gameObject);
        }

        // ------------------------------------------------------------ layout

        private void Build(GameObject template)
        {
            work = JsonUtility.FromJson<PlayerLook>(JsonUtility.ToJson(Look.Current));
            titleGroup = template.GetComponentInParent<ButtonGroup>(true);
            if (titleGroup != null)
            {
                titleGroup.enabled = false;
                foreach (var b in titleGroup.buttons.Where(b => b != null))
                    foreach (var c in b.GetComponents<Collider2D>().Where(c => c.enabled)) { c.enabled = false; disabled.Add(c); }
            }
            var baseSr = template.GetComponent<SpriteRenderer>();
            layer = baseSr.sortingLayerID;
            order = baseSr.sortingOrder + 60;
            fontSource = template.GetComponentsInChildren<TextMeshPro>(true).OrderBy(l => l.name).FirstOrDefault();

            var cam = Camera.main;
            float H = cam != null ? cam.orthographicSize * 2f : 34f, W = H * (cam != null ? cam.aspect : 1.78f);
            var c0 = cam != null ? (Vector3)(Vector2)cam.transform.position : Vector3.zero;
            float z = template.transform.position.z - 0.4f;
            transform.position = new Vector3(c0.x, c0.y, z);

            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            float pw = W * 0.9f, ph = H * 0.88f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            Text("CUSTOMIZE YOUR PLAYER", new Vector2(0f, ph / 2f - 1.6f), 1.1f, pw - 4f, Color.white);

            // live preview: a copy of the title-screen player, larger
            var player = FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(Look.IsPlayerOne);
            float previewX = -pw / 2f + pw * 0.2f;
            Box("Stage", new Vector2(previewX, -0.5f), new Vector2(pw * 0.32f, ph * 0.72f), new Color(0.62f, 0.86f, 0.97f, 1f), 2);
            if (player != null)
            {
                titleRig = Look.RigOf(player);
                var copy = Instantiate(titleRig.gameObject, transform);
                foreach (var lk in copy.GetComponentsInChildren<LookKeeper>(true)) DestroyImmediate(lk);
                preview = copy.transform;
                preview.localScale = titleRig.lossyScale * 2.6f;
                var b = Bounds(preview);
                preview.position += new Vector3(transform.position.x + previewX - b.center.x, transform.position.y - 0.5f - b.center.y, z - 0.05f - preview.position.z);
                foreach (var r in preview.GetComponentsInChildren<Renderer>(true)) { r.sortingLayerID = layer; r.sortingOrder = order + 10 + r.sortingOrder; }
            }

            // option rows
            float top = ph / 2f - 3.6f, bottom = -ph / 2f + 4.4f;
            float rh = (top - bottom) / rows.Count;
            float colL = -pw / 2f + pw * 0.42f, colV = pw / 2f - pw * 0.2f;
            for (int i = 0; i < rows.Count; i++)
            {
                int row = i;
                float y = top - rh * (i + 0.5f);
                float rowL = colL - 1f, rowR = colV + pw * 0.16f;
                rowBoxes.Add(Box("Row", new Vector2((rowL + rowR) / 2f, y), new Vector2(rowR - rowL, rh * 0.86f), new Color(1f, 1f, 1f, 0f), 2));
                Text(rows[i].label, new Vector2(colL, y), 0.75f, pw * 0.3f, Color.white, TextAlignmentOptions.Left, pivotLeft: true);
                values.Add(Text("", new Vector2(colV, y), 0.75f, pw * 0.22f, Color.white));
                swatches.Add(Box("Swatch", new Vector2(colV - pw * 0.165f, y), new Vector2(rh * 0.6f, rh * 0.6f), Color.white, 4));
                Button("<", new Vector2(colV - pw * 0.12f, y), new Vector2(rh * 0.8f, rh * 0.8f), () => { selected = row; Step(row, -1); });
                Button(">", new Vector2(colV + pw * 0.12f, y), new Vector2(rh * 0.8f, rh * 0.8f), () => { selected = row; Step(row, 1); });
            }
            // footer
            float fy = -ph / 2f + 2.2f;
            footer.Add(Button("RANDOMIZE", new Vector2(-pw * 0.22f, fy), new Vector2(pw * 0.2f, 2.6f), Randomize));
            footer.Add(Button("RESET", new Vector2(0f, fy), new Vector2(pw * 0.2f, 2.6f), ResetLook));
            footer.Add(Button("DONE", new Vector2(pw * 0.22f, fy), new Vector2(pw * 0.2f, 2.6f), () => Close(true)));
            Refresh();
        }

        private SpriteRenderer Box(string name, Vector2 at, Vector2 size, Color color, int o)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.transform.localPosition = new Vector3(at.x, at.y, -0.01f * o);
            sr.sprite = ContinueMenu.Rounded();
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = size;
            sr.color = color;
            sr.sortingLayerID = layer;
            sr.sortingOrder = order + o;
            return sr;
        }

        private TextMeshPro Text(string s, Vector2 at, float size, float width, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center, bool pivotLeft = false)
        {
            var t = Instantiate(fontSource.gameObject, transform).GetComponent<TextMeshPro>();
            t.name = "Text";
            var ls = fontSource.transform.lossyScale;
            t.transform.localScale = ls;
            t.rectTransform.pivot = new Vector2(pivotLeft ? 0f : 0.5f, 0.5f);
            t.transform.localPosition = new Vector3(at.x, at.y, -0.2f);
            t.text = s;
            t.alignment = align;
            t.enableWordWrapping = false;
            t.enableAutoSizing = false;
            t.fontSize = fontSource.fontSize * size;
            t.rectTransform.sizeDelta = new Vector2(width / ls.x, 3f / ls.y);
            t.color = color;
            var r = t.GetComponent<Renderer>();
            r.sortingLayerID = layer;
            r.sortingOrder = order + 8;
            return t;
        }

        private SpriteRenderer Button(string label, Vector2 at, Vector2 size, Action act)
        {
            var normal = new Color(0.16f, 0.66f, 0.9f, 1f);
            var sr = Box("Button " + label, at, size, normal, 5);
            var col = sr.gameObject.AddComponent<BoxCollider2D>();
            col.size = size;
            var w = sr.gameObject.AddComponent<CreatorWidget>();
            w.Box = sr; w.Normal = normal; w.Hover = new Color(0.4f, 0.8f, 1f, 1f); w.OnClick = act;
            Text(label, at, label.Length <= 1 ? 0.8f : 0.8f, size.x, Color.white);
            return sr;
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
            Refresh();
        }

        private void Randomize() { work = Look.Randomized(); Refresh(); }
        private void ResetLook() { work = new PlayerLook(); Refresh(); }

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

        private void Refresh()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                int v = Get(rows[i].field);
                values[i].text = ValueText(rows[i].field, v).ToUpperInvariant();
                var hex = SwatchHex(rows[i].field, v);
                swatches[i].enabled = hex != null;
                if (hex != null) swatches[i].color = Vfx.Hex(hex);
                rowBoxes[i].color = new Color(1f, 1f, 1f, i == selected ? 0.16f : 0f);
            }
            for (int i = 0; i < footer.Count; i++)
                footer[i].transform.localScale = Vector3.one * (selected == rows.Count && i == footerSel ? 1.08f : 1f);
            if (preview != null) Look.Apply(preview, work);
            if (titleRig != null) Look.Apply(titleRig, work);
        }

        // ------------------------------------------------------------ keyboard and gamepad

        private void Update()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            int dy = 0, dx = 0;
            bool ok = false, back = false;
            if (kb != null)
            {
                if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) dy = -1;
                if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) dy = 1;
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) dx = -1;
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) dx = 1;
                ok = kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
                back = kb.escapeKey.wasPressedThisFrame;
            }
            if (pad != null)
            {
                if (pad.dpad.up.wasPressedThisFrame) dy = -1;
                if (pad.dpad.down.wasPressedThisFrame) dy = 1;
                if (pad.dpad.left.wasPressedThisFrame) dx = -1;
                if (pad.dpad.right.wasPressedThisFrame) dx = 1;
                var st = pad.leftStick.ReadValue();
                stickTimer -= Time.unscaledDeltaTime;
                if (stickTimer <= 0f && st.magnitude > 0.6f)
                {
                    if (Mathf.Abs(st.y) > Mathf.Abs(st.x)) dy = st.y > 0 ? -1 : 1; else dx = st.x > 0 ? 1 : -1;
                    stickTimer = 0.22f;
                }
                ok |= pad.buttonSouth.wasPressedThisFrame;
                back |= pad.buttonEast.wasPressedThisFrame;
            }
            if (back) { Close(true); return; }
            if (dy != 0) { selected = Mathf.Clamp(selected + dy, 0, rows.Count); Refresh(); }
            if (dx != 0)
            {
                if (selected < rows.Count) Step(selected, dx);
                else { footerSel = Mathf.Clamp(footerSel + dx, 0, footer.Count - 1); Refresh(); }
            }
            if (ok)
            {
                if (selected < rows.Count) Step(selected, 1);
                else footer[footerSel].GetComponent<CreatorWidget>().OnClick?.Invoke();
            }
        }
    }
}

using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HangtimeOvertime.Patches
{
    // The game's own menu buttons (title menu, pause menu, lose and win screens, and the mod's buttons cloned from them) as
    // painted signs: the hand-drawn panel shape with a marker outline and a drop shadow instead of the game's translucent
    // card, the menus' lettering, and drawn by the UI camera (no grading). The button itself is untouched: the game still
    // scales it on hover, clicks it and animates it in and out. CLASSIC and the "go again" buttons are gold.
    internal static class SignSkin
    {
        public static void Init() => SceneManager.sceneLoaded += (scene, _) => Runner.Instance.StartCoroutine(Later());

        private static IEnumerator Later()
        {
            yield return null;                          // the mod's own title buttons are added on load too
            yield return null;
            ApplyAll();
        }

        public static void ApplyAll()
        {
            if (!UiArt.Ready) return;
            int n = 0;
            foreach (var b in Object.FindObjectsByType<ButtonController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b.gameObject.scene.IsValid() && Apply(b)) n++;
            if (n > 0) Plugin.Log.LogInfo($"Signs: {n} game buttons painted");
        }

        private static (Color fill, Color text, float tilt) Style(ButtonController b)
        {
            string n = b.name.ToLowerInvariant();
            var blue = UiArt.Blue;
            if (n.Contains("1 player") || n.Contains("restart") || n.Contains("resume") || n.Contains("keep going")) return (UiArt.Gold, UiArt.Dark, -1.2f);
            if (n == "quit") return (new Color(0.24f, 0.16f, 0.28f, 1f), UiArt.Ink, 0f);
            if (n.Contains("versus") || n.Contains("customize") || n.Contains("multiplayer")) return (new Color(0.15f, 0.59f, 0.67f, 1f), UiArt.Ink, 0f);
            return (blue, UiArt.Ink, 0f);
        }

        public static bool Apply(ButtonController b)
        {
            if (b == null || b.transform.Find("Overtime Sign") != null) return false;
            var card = b.GetComponentsInChildren<SpriteRenderer>(true).FirstOrDefault(r => r.name == "Sprite" && r.transform.parent == b.transform);
            if (card == null) return false;
            var (fill, text, tilt) = Style(b);
            var cam = Camera.main;
            bool uiCam = cam != null && UiCamera.Ensure(cam);

            // the sign: the card's world size, under the button so the game's hover scaling and animations move it too
            float scale = Mathf.Max(0.0001f, Mathf.Abs(b.transform.lossyScale.x));
            var size = (Vector2)card.bounds.size;                                                   // world units (the root undoes the button's scale)
            var root = new GameObject("Overtime Sign");
            root.transform.SetParent(b.transform, false);
            root.transform.position = card.bounds.center;
            root.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
            root.transform.localScale = Vector3.one / scale;
            int v = Mathf.Abs(b.name.GetHashCode()) % UiArt.Variants;
            SpriteRenderer Part(string name, Sprite s, Color c, Vector3 at, int dOrder)
            {
                var r = new GameObject(name).AddComponent<SpriteRenderer>();
                r.transform.SetParent(root.transform, false);
                r.transform.localPosition = at;
                UiArt.MakeUnlit(r);
                r.sprite = s; r.drawMode = SpriteDrawMode.Sliced; r.size = size; r.color = UiArt.Paint(c);
                r.sortingLayerID = card.sortingLayerID; r.sortingOrder = card.sortingOrder + dOrder;
                return r;
            }
            Part("Shadow", UiArt.Fill(v), UiArt.Shadow, new Vector3(0.22f, -0.22f, 0.002f), 0);
            Part("Fill", UiArt.Fill(v), fill, Vector3.zero, 0);
            Part("Outline", UiArt.Line(v), UiArt.Ink, new Vector3(0f, 0f, -0.001f), 1);
            card.enabled = false;

            // the lettering
            var font = UiArt.Lettering;
            foreach (var t in b.GetComponentsInChildren<TMP_Text>(true))
            {
                if (font != null) { t.font = font; t.fontSharedMaterial = font.material; }
                var c = text; c.a = t.color.a;
                t.color = c;
                var mr = t.GetComponent<Renderer>();                     // in front of the (opaque) sign; the game's card was see-through
                if (mr != null) { mr.sortingLayerID = card.sortingLayerID; mr.sortingOrder = Mathf.Max(mr.sortingOrder, card.sortingOrder + 3); }
                t.transform.localRotation = Quaternion.Euler(0f, 0f, tilt);
            }
            // the painted parts and the lettering go on the menus' layer; the button itself (its click area) stays where the
            // main camera sends it the mouse
            if (uiCam) foreach (Transform child in b.transform) UiCamera.SetLayer(child.gameObject);
            return true;
        }
    }
}

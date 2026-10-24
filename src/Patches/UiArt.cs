using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // The hand-drawn UI pieces (tools/ui_art.py, shipped in the plugin's ui folder): wobbly panel fills and outlines,
    // 9-sliced so they stretch to any box, and white icons. Everything is white so the kit tints it. Missing files fall back
    // to the plain rounded box (ContinueMenu.Rounded) and no icon.
    internal static class UiArt
    {
        // tools/ui_art.py: PANEL 384 px, slice BORDER 96 px; the corner (inset 14 + radius 64) is about 0.7 layout units
        private const float PanelPpu = 110f, Border = 96f, IconPpu = 128f;
        public const int Variants = 3;

        // the menus' colours (the mockup's palette), as they look on screen (the menus' sprites are unlit: see Unlit)
        private static Color Screen(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);
        public static readonly Color Navy = Screen(0.055f, 0.17f, 0.30f, 0.96f);
        public static readonly Color Blue = Screen(0.12f, 0.34f, 0.56f);
        public static readonly Color BlueHover = Screen(0.2f, 0.47f, 0.72f);
        public static readonly Color Inner = Screen(0.03f, 0.11f, 0.2f);
        public static readonly Color Ink = Screen(1f, 0.965f, 0.88f);
        public static readonly Color Gold = Screen(1f, 0.82f, 0.29f);
        public static readonly Color Orange = Screen(1f, 0.54f, 0.24f);
        public static readonly Color Selected = Screen(1f, 0.6f, 0.2f);
        public static readonly Color Shadow = new Color(0f, 0.02f, 0.06f, 0.45f);
        public static readonly Color Dark = Screen(0.03f, 0.094f, 0.17f);

        private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        private static string Dir => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "ui");

        public static Sprite Fill(int variant) => Load($"panel_fill_{(variant % Variants + Variants) % Variants}", true);
        public static Sprite Line(int variant) => Load($"panel_line_{(variant % Variants + Variants) % Variants}", true);
        public static Sprite Icon(string name) => Load("icon_" + name, false);

        private static Sprite Load(string name, bool sliced)
        {
            if (cache.TryGetValue(name, out var s)) return s;
            var path = Path.Combine(Dir, name + ".png");
            Sprite sprite = null;
            if (File.Exists(path))
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { hideFlags = HideFlags.DontUnloadUnusedAsset, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
                if (tex.LoadImage(File.ReadAllBytes(path)))
                {
                    sprite = sliced
                        ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), PanelPpu, 0, SpriteMeshType.FullRect, new Vector4(Border, Border, Border, Border))
                        : Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), IconPpu);
                    sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    sprite.name = name;
                }
            }
            else Plugin.Log.LogWarning("UI art missing: " + path);
            cache[name] = sprite;
            return sprite;
        }

        public static bool Ready => Fill(0) != null && Line(0) != null;

        // the menus' lettering: Bangers (SIL Open Font License, ui/OFL-Bangers.txt), the chunky hand-lettered face the mod's
        // emblems and logo use, made into a dynamic TextMeshPro font at runtime; null (the game's own font) if that fails
        private static TMPro.TMP_FontAsset lettering;
        private static bool letteringTried;
        public const float LetteringScale = 1.0f;          // its size against the game's menu font at the same setting
        public static TMPro.TMP_FontAsset Lettering
        {
            get
            {
                if (letteringTried) return lettering;
                letteringTried = true;
                var path = Path.Combine(Dir, "Bangers-Regular.ttf");
                if (!File.Exists(path)) { Plugin.Log.LogWarning("UI font missing: " + path); return null; }
                try
                {
                    // CreateFontAsset(string fontFilePath, int faceIndex, int samplingPointSize, int atlasPadding, GlyphRenderMode, int w, int h, ...)
                    var m = typeof(TMPro.TMP_FontAsset).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                        .FirstOrDefault(x => x.Name == "CreateFontAsset" && x.GetParameters().Length >= 7 && x.GetParameters()[0].ParameterType == typeof(string) && x.GetParameters()[1].ParameterType == typeof(int));
                    if (m == null) { Plugin.Log.LogWarning("UI font: this TextMeshPro can't load a font file"); return null; }
                    var ps = m.GetParameters();
                    var args = new object[ps.Length];
                    for (int i = 0; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                    args[0] = path; args[1] = 0; args[2] = 90; args[3] = 9;
                    args[4] = System.Enum.Parse(ps[4].ParameterType, "SDFAA");           // GlyphRenderMode (TextCoreFontEngineModule)
                    args[5] = 1024; args[6] = 1024;
                    lettering = (TMPro.TMP_FontAsset)m.Invoke(null, args);
                    if (lettering != null)
                    {
                        lettering.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        lettering.name = "Bangers (Overtime)";
                    }
                    Plugin.Log.LogInfo("UI font: " + (lettering != null ? $"Bangers loaded (material {lettering.material?.shader?.name})" : "not created"));
                }
                catch (System.Exception e) { Plugin.Log.LogWarning("UI font: " + e.Message); lettering = null; }
                return lettering;
            }
        }

        // the game's 2D lights tint ordinary sprites (the title screen is lit teal): the menus' sprites use URP's unlit sprite
        // shader so their colours stay as chosen, like the text (TextMeshPro ignores lights)
        private static Material unlit;
        private static bool unlitTried;
        public static Material Unlit
        {
            get
            {
                if (unlitTried) return unlit;
                unlitTried = true;
                var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
                if (sh != null) unlit = new Material(sh) { hideFlags = HideFlags.DontUnloadUnusedAsset, name = "Overtime UI (unlit)" };
                Plugin.Log.LogInfo("UI sprites: " + (sh != null ? sh.name : "no unlit sprite shader found, lit by the scene"));
                return unlit;
            }
        }

        public static void MakeUnlit(SpriteRenderer r) { if (Unlit != null) r.sharedMaterial = Unlit; }

        // the colour to give a menu sprite: older menus' colours remapped onto the palette. Drawn by the UI camera (no grading)
        // and unlit, a sprite shows exactly its colour (measured with Debugging.ColorTest).
        public static Color Paint(Color c) => Remap(c);

        // the smallest box the drawn corners still fit (smaller ones keep the plain rounded box)
        public const float MinSize = 1.05f;

        // older menus' colours mapped onto the new palette, so every menu changes at once
        public static Color Remap(Color c)
        {
            bool Is(Color a, float r, float g, float b) => Mathf.Abs(a.r - r) < 0.02f && Mathf.Abs(a.g - g) < 0.02f && Mathf.Abs(a.b - b) < 0.02f;
            if (Is(c, 0.05f, 0.42f, 0.66f)) return new Color(Navy.r, Navy.g, Navy.b, c.a);             // the panels' blue
            if (Is(c, 0.16f, 0.66f, 0.9f)) return Blue;                                                 // buttons
            if (Is(c, 0.4f, 0.8f, 1f)) return BlueHover;                                                // button hover
            if (Is(c, 0.16f, 0.6f, 0.86f)) return Blue;                                                 // an unselected tab
            if (Is(c, 0.02f, 0.2f, 0.34f)) return new Color(Inner.r, Inner.g, Inner.b, c.a);             // inner boxes / fields
            if (Is(c, 0.96f, 0.55f, 0.12f) || Is(c, 0.85f, 0.45f, 0.08f)) return Selected;                // the chosen tab / row
            return c;
        }
    }
}

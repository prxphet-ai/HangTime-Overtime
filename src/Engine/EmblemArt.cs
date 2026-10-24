using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HangtimeOvertime.Core;
using UnityEngine;

namespace HangtimeOvertime.Engine
{
    // The community tournaments' emblem builder: the same pictures tools/emblems.py draws for the mod's own teams, put
    // together in game from its pieces (emblems/parts: each mascot alone with its accent parts black, and a letter atlas per
    // lettering font), with the same layouts (name over the mascot; first word over and the rest under; mascot with the
    // words under it, left and right). Letters outside the atlas are left out. 600 x 430, white ink plus the accent colour.
    internal static class EmblemArt
    {
        private const int W = 600, H = 430;
        private const float PartScale = 1.3f;           // tools/emblems.py PART_SCALE
        private const float GlyphSize = 120f;           // GLYPH_SIZE
        private static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, (Color32[] px, int w, int h)> images = new Dictionary<string, (Color32[], int, int)>();
        private static readonly Dictionary<string, Dictionary<char, Glyph>> atlases = new Dictionary<string, Dictionary<char, Glyph>>();

        private struct Glyph { public int X, Y, W, H; public float Left, Top, Advance; }

        private static string Dir => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "emblems", "parts");

        public static Texture2D Make(TourEmblem e, string name)
        {
            string key = $"{e.mascot}|{e.layout}|{e.font}|{e.accent}|{name}";
            if (cache.TryGetValue(key, out var have) && have != null) return have;
            var tex = Compose(e, (name ?? "").ToUpperInvariant());
            if (tex == null) return null;
            if (cache.Count > 64) { foreach (var t in cache.Values) if (t != null) UnityEngine.Object.Destroy(t); cache.Clear(); }
            cache[key] = tex;
            return tex;
        }

        private static Texture2D Compose(TourEmblem e, string name)
        {
            var mascot = Image("mascot_" + e.mascot);
            var glyphs = Atlas(e.font);
            var letters = Image("glyphs_" + e.font);
            if (mascot.px == null || glyphs == null || letters.px == null) { Plugin.Log.LogWarning("Emblem parts missing in " + Dir); return null; }
            var canvas = new Color[W * H];                                  // top-down, straight alpha
            var accent = Light(Vfx.Hex(e.accent), 0.35f);
            var rng = new System.Random(name.GetHashCode() ^ e.mascot.GetHashCode());
            bool marker = e.font == "marker";
            var words = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string first = words.Length > 0 ? words[0] : "", rest = string.Join(" ", words.Skip(1));
            void Txt(string s, float cx, float cy, float size, float maxW) => DrawText(canvas, s, cx, cy, size, maxW, glyphs, letters, rng);
            void Masc(float cx, float cy, float s) => DrawMascot(canvas, mascot, cx, cy, s / PartScale, accent);
            switch (e.layout)
            {
                case "top":
                    Txt(name, W / 2f, 80f, 76f, W - 30f);
                    Masc(W / 2f, 284f, 1.22f);
                    break;
                case "stacked":
                    Txt(first, W / 2f, 76f, 78f, W - 50f);
                    Masc(W / 2f, 236f, 0.92f);
                    Txt(rest, W / 2f, marker ? 380f : 396f, 62f, W - 80f);
                    break;
                default:                                                     // split
                    Masc(W / 2f, 196f, 1.3f);
                    float y = marker ? 378f : 395f;
                    if (words.Length <= 1) Txt(first, W / 2f, y + 5f, 70f, W - 40f);
                    else { Txt(first, W * 0.27f, y, 62f, W * 0.48f); Txt(rest, W * 0.73f, y, 62f, W * 0.48f); }
                    break;
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontUnloadUnusedAsset, wrapMode = TextureWrapMode.Clamp, name = "Emblem " + name };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++) px[(H - 1 - y) * W + x] = canvas[y * W + x];   // Unity rows go bottom-up
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        private static Color Light(Color c, float k) => new Color(c.r + (1f - c.r) * k, c.g + (1f - c.g) * k, c.b + (1f - c.b) * k, 1f);

        // the mascot centred on (cx, cy), scaled by k from its part; black in the part is the accent colour
        private static void DrawMascot(Color[] canvas, (Color32[] px, int w, int h) img, float cx, float cy, float k, Color accent)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - img.w * k / 2f)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + img.w * k / 2f));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - img.h * k / 2f)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + img.h * k / 2f));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var s = Sample(img, (x - cx) / k + img.w / 2f, (y - cy) / k + img.h / 2f);
                    if (s.a <= 0.003f) continue;
                    var col = Color.Lerp(accent, Color.white, s.r);
                    col.a = s.a;
                    Over(canvas, x, y, col);
                }
        }

        // hand lettering: letters side by side at `size`, shrunk to fit maxW, each turned a little and lifted a little
        private static void DrawText(Color[] canvas, string s, float cx, float cy, float size, float maxW, Dictionary<char, Glyph> glyphs, (Color32[] px, int w, int h) atlas, System.Random rng)
        {
            const float spacing = 0.92f;
            if (string.IsNullOrEmpty(s)) return;
            float Width(char c, float sz) => c == ' ' ? sz * 0.35f : glyphs.TryGetValue(c, out var g) ? g.Advance * sz / GlyphSize : 0f;
            float total = s.Sum(c => Width(c, size)) * spacing;
            if (total > maxW) { size *= maxW / total; total = maxW; }
            float k = size / GlyphSize, x = cx - total / 2f;
            foreach (char c in s)
            {
                if (c != ' ' && glyphs.TryGetValue(c, out var g))
                {
                    float left = x - 2f * k;                                // the pen is the ink's left; the cell starts 2 px before it
                    float top = cy - 0.65f * size + g.Top * k + (float)(rng.NextDouble() * 3.0 - 1.5);
                    float ang = (float)(rng.NextDouble() * 12.0 - 6.0) * Mathf.Deg2Rad;
                    DrawGlyph(canvas, atlas, g, left, top, k, ang);
                }
                x += Width(c, size) * spacing;
            }
        }

        private static void DrawGlyph(Color[] canvas, (Color32[] px, int w, int h) atlas, Glyph g, float left, float top, float k, float ang)
        {
            float gw = g.W * k, gh = g.H * k, cx = left + gw / 2f, cy = top + gh / 2f;
            float cos = Mathf.Cos(ang), sin = Mathf.Sin(ang), r = Mathf.Sqrt(gw * gw + gh * gh) / 2f + 1f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + r));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + r));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float u = (dx * cos + dy * sin) / k + g.W / 2f, v = (-dx * sin + dy * cos) / k + g.H / 2f;   // back into the cell
                    if (u < 0f || v < 0f || u > g.W - 1 || v > g.H - 1) continue;
                    var s = Sample(atlas, g.X + u, g.Y + v);
                    if (s.a <= 0.003f) continue;
                    Over(canvas, x, y, new Color(1f, 1f, 1f, s.a));
                }
        }

        private static void Over(Color[] canvas, int x, int y, Color src)
        {
            int i = y * W + x;
            var d = canvas[i];
            float a = src.a + d.a * (1f - src.a);
            if (a <= 0f) return;
            canvas[i] = new Color((src.r * src.a + d.r * d.a * (1f - src.a)) / a, (src.g * src.a + d.g * d.a * (1f - src.a)) / a, (src.b * src.a + d.b * d.a * (1f - src.a)) / a, a);
        }

        // bilinear, in top-down pixel coordinates of an image loaded bottom-up
        private static Color Sample((Color32[] px, int w, int h) img, float x, float y)
        {
            x -= 0.5f; y -= 0.5f;
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            Color P(int px, int py)
            {
                if (px < 0 || py < 0 || px >= img.w || py >= img.h) return new Color(1f, 1f, 1f, 0f);
                return img.px[(img.h - 1 - py) * img.w + px];
            }
            var a = P(xi, yi); var b = P(xi + 1, yi); var c = P(xi, yi + 1); var d = P(xi + 1, yi + 1);
            // weight colours by alpha, so the transparent white around the ink doesn't wash out the accent
            float wa = (1 - fx) * (1 - fy) * a.a, wb = fx * (1 - fy) * b.a, wc = (1 - fx) * fy * c.a, wd = fx * fy * d.a;
            float alpha = (1 - fx) * (1 - fy) * a.a + fx * (1 - fy) * b.a + (1 - fx) * fy * c.a + fx * fy * d.a;
            float wsum = wa + wb + wc + wd;
            if (wsum <= 0f) return new Color(1f, 1f, 1f, 0f);
            float r = (a.r * wa + b.r * wb + c.r * wc + d.r * wd) / wsum;
            return new Color(r, r, r, alpha);
        }

        private static (Color32[] px, int w, int h) Image(string name)
        {
            if (images.TryGetValue(name, out var have)) return have;
            var path = Path.Combine(Dir, name + ".png");
            (Color32[], int, int) r = (null, 0, 0);
            if (File.Exists(path))
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(File.ReadAllBytes(path))) r = (tex.GetPixels32(), tex.width, tex.height);
                UnityEngine.Object.Destroy(tex);
            }
            images[name] = r;
            return r;
        }

        private static Dictionary<char, Glyph> Atlas(string font)
        {
            if (atlases.TryGetValue(font, out var have)) return have;
            var path = Path.Combine(Dir, "glyphs_" + font + ".txt");
            Dictionary<char, Glyph> map = null;
            if (File.Exists(path))
            {
                map = new Dictionary<char, Glyph>();
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                foreach (var line in File.ReadAllLines(path).Where(l => l.Length > 0 && l[0] != '#'))
                {
                    var f = line.Split(' ');
                    if (f.Length < 8) continue;
                    map[(char)int.Parse(f[0])] = new Glyph { X = int.Parse(f[1]), Y = int.Parse(f[2]), W = int.Parse(f[3]), H = int.Parse(f[4]),
                        Left = float.Parse(f[5], inv), Top = float.Parse(f[6], inv), Advance = float.Parse(f[7], inv) };
                }
            }
            atlases[font] = map;
            return map;
        }

        public static Sprite SpriteOf(Texture2D tex, float pixelsPerUnit)
        {
            var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
            s.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return s;
        }
    }
}

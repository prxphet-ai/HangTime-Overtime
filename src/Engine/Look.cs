using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Engine
{
    // The player's appearance (cosmetic only: nothing here touches stats, perks or hitboxes).
    // Indexes into the option lists below; 0 is always "as the game draws it".
    [Serializable]
    public class PlayerLook
    {
        public int version = 1;
        public int hairStyle, hairColor, skin, jersey, trim, number = 13, build, headband, wristbands, kneePads, shoes;
    }

    internal static class Look
    {
        public static PlayerLook Current = new PlayerLook();
        private static string PathOf => Path.Combine(Application.persistentDataPath, "HangtimeOvertime_look.json");

        // ------------------------------------------------------------ options

        public static readonly (string name, string sprite)[] HairStyles =
        {
            ("Spiky", null), ("Round", "Round hair_0"), ("Big", "Big hair_0"), ("Flame", "Flame hair_0"), ("Swoop", "Overhang hair_0"), ("Long", "Back hair_0"),
        };

        public static readonly (string name, string hex)[] HairColors =
        {
            ("Original", null), ("Black", "23232B"), ("Dark brown", "4A2E1A"), ("Brown", "7A4A28"), ("Auburn", "9A3A1A"), ("Ginger", "FF7323"),
            ("Blonde", "F2D06A"), ("Silver", "D2D4DE"), ("White", "FFFFFF"), ("Red", "D8302A"), ("Pink", "FF8AC8"), ("Purple", "8A4AD0"),
            ("Blue", "2F62DA"), ("Teal", "2ABAB0"), ("Green", "3AB86A"),
        };

        // multipliers on the game's skin colour (it can only be darkened, so "Original" is the lightest)
        public static readonly (string name, string hex)[] SkinTones =
        {
            ("Original", null), ("Fair", "FFF0E6"), ("Light", "F4D9C2"), ("Medium", "DDB08A"), ("Tan", "C08F68"), ("Brown", "9A6A48"), ("Deep", "754C33"), ("Dark", "573826"),
        };

        public static readonly (string name, string hex)[] UniformColors =
        {
            ("Original", null), ("White", "F4F6FA"), ("Black", "2A2A32"), ("Red", "E0383A"), ("Orange", "FF8A2A"), ("Yellow", "FFD23A"), ("Lime", "9AE04A"),
            ("Green", "2EAA5A"), ("Teal", "2AC2B4"), ("Sky", "28B7FF"), ("Navy", "2A3E8A"), ("Purple", "8A52D8"), ("Pink", "FF7AB8"), ("Gray", "8A8E98"),
        };

        public static readonly (string name, float x, float y)[] Builds =
        {
            ("Regular", 1f, 1f), ("Slim", 0.9f, 1.02f), ("Broad", 1.12f, 1f), ("Tall", 0.98f, 1.08f), ("Compact", 1.05f, 0.93f),
        };

        // headband / wristbands / knee pads / shoes: 0 = none (or original shoes), then the uniform palette
        public static string AccessoryName(int i) => i == 0 ? "None" : UniformColors[i].name;
        public static string ShoeName(int i) => UniformColors[i].name;

        public static int Count(string field)
        {
            switch (field)
            {
                case "hairStyle": return HairStyles.Length;
                case "hairColor": return HairColors.Length;
                case "skin": return SkinTones.Length;
                case "build": return Builds.Length;
                case "number": return 100;
                default: return UniformColors.Length;
            }
        }

        public static PlayerLook Randomized()
        {
            var r = new System.Random();
            return new PlayerLook
            {
                hairStyle = r.Next(HairStyles.Length), hairColor = r.Next(1, HairColors.Length), skin = r.Next(SkinTones.Length),
                jersey = r.Next(1, UniformColors.Length), trim = r.Next(1, UniformColors.Length), number = r.Next(0, 100),
                build = r.Next(Builds.Length), headband = r.Next(3) == 0 ? r.Next(1, UniformColors.Length) : 0,
                wristbands = r.Next(3) == 0 ? r.Next(1, UniformColors.Length) : 0, kneePads = r.Next(3) == 0 ? r.Next(1, UniformColors.Length) : 0,
                shoes = r.Next(2) == 0 ? 0 : r.Next(1, UniformColors.Length),
            };
        }

        // ------------------------------------------------------------ saving

        public static void Load()
        {
            try
            {
                if (File.Exists(PathOf)) Current = JsonUtility.FromJson<PlayerLook>(File.ReadAllText(PathOf)) ?? new PlayerLook();
                Clamp(Current);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read the saved appearance (" + e.Message + "); using the default look");
                Current = new PlayerLook();
            }
        }

        public static void Save()
        {
            try { File.WriteAllText(PathOf, JsonUtility.ToJson(Current, true)); }
            catch (Exception e) { Plugin.Log.LogWarning("Could not save the appearance: " + e.Message); }
        }

        private static void Clamp(PlayerLook l)
        {
            int C(int v, string f) => Mathf.Clamp(v, 0, Count(f) - 1);
            l.hairStyle = C(l.hairStyle, "hairStyle"); l.hairColor = C(l.hairColor, "hairColor"); l.skin = C(l.skin, "skin");
            l.jersey = C(l.jersey, "jersey"); l.trim = C(l.trim, "trim"); l.number = C(l.number, "number"); l.build = C(l.build, "build");
            l.headband = C(l.headband, "x"); l.wristbands = C(l.wristbands, "x"); l.kneePads = C(l.kneePads, "x"); l.shoes = C(l.shoes, "x");
        }

        // ------------------------------------------------------------ applying it to a character rig ("Anime sprite" and below)

        private static Color Hex(string h) => Vfx.Hex(h);

        public static void Apply(Transform rig, PlayerLook l)
        {
            if (rig == null || l == null) return;
            var keeper = rig.GetComponent<LookKeeper>() ?? rig.gameObject.AddComponent<LookKeeper>();
            keeper.Remember();                                   // the game's own colours/sprites, once
            keeper.Restore();                                    // start from them, then apply the look

            var parts = rig.GetComponentsInChildren<SpriteRenderer>(true);
            SpriteRenderer Part(string n) => parts.FirstOrDefault(p => p.name == n);

            // hair
            var back = Part("Hair_0");
            var front = Part("Front hair");
            if (back != null)
            {
                var style = HairStyles[l.hairStyle];
                if (style.sprite != null)
                {
                    var donor = HairDonor(style.sprite);       // a game rig wearing this hair: same sprite, placed and scaled for it
                    if (donor != null)
                    {
                        back.sprite = donor.sprite;
                        back.transform.localPosition = donor.transform.localPosition;
                        back.transform.localRotation = donor.transform.localRotation;
                        back.transform.localScale = donor.transform.localScale;
                    }
                }
                if (l.hairColor > 0)
                {
                    var c = Hex(HairColors[l.hairColor].hex);
                    if (style.sprite == null) back.sprite = Tintable(back.sprite);   // the player's own hair is drawn in colour
                    back.color = c;
                    if (front != null) front.color = c;
                }
                else if (style.sprite != null)
                {
                    back.color = front != null ? front.color : new Color(0.1f, 0.1f, 0.15f);   // match the original fringe
                    if (front != null && front.color.r + front.color.g + front.color.b < 0.6f) back.color = front.color * 1.4f;
                }
            }

            // some rigs fill the back of the spiky hair with an extra circle: hidden for other styles, hair-coloured for spiky
            foreach (var fill in parts.Where(p => p.name == "Hair_0 (1)"))
            {
                if (HairStyles[l.hairStyle].sprite != null) fill.color = new Color(0f, 0f, 0f, 0f);
                else if (l.hairColor > 0) fill.color = Hex(HairColors[l.hairColor].hex) * 0.8f;
            }

            // skin: multiply the game's own skin shading
            if (l.skin > 0)
            {
                var tone = Hex(SkinTones[l.skin].hex);
                foreach (var p in parts.Where(p => p.name == "Head_0" || p.name.StartsWith("Shoulder") || p.name == "Forearm_0" || p.name == "Hand_0" || p.name == "Calf_0"))
                    p.color = keeper.Original(p) * tone;
            }

            // uniform: jersey (primary), shorts and number (secondary)
            var core = Part("Core_0");
            if (core != null && l.jersey > 0) core.color = Hex(UniformColors[l.jersey].hex);
            if (l.trim > 0)
            {
                var t = Hex(UniformColors[l.trim].hex);
                foreach (var p in parts.Where(p => p.name.StartsWith("Thigh_0")))
                    p.color = p.name == "Thigh_0" ? t : Color.Lerp(t, Color.black, 0.12f);
            }
            foreach (var num in rig.GetComponentsInChildren<TextMeshPro>(true).Where(t => t.name.StartsWith("Number")))
            {
                num.text = l.number.ToString();
                if (l.trim > 0) num.color = Color.Lerp(Hex(UniformColors[l.trim].hex), Color.white, 0.45f);
            }

            // build: proportions of the body (the animation's own scale is kept, see LookKeeper)
            keeper.Build = new Vector2(Builds[l.build].x, Builds[l.build].y);

            // shoes
            if (l.shoes > 0)
                foreach (var p in parts.Where(p => p.name == "Foot_0")) p.color = keeper.Original(p) * Hex(UniformColors[l.shoes].hex);

            // extras
            Accessory(Part("Head_0"), "Overtime headband", l.headband, new Vector2(1.0f, 0.13f), new Vector2(0f, 0.3f), 1, front);
            foreach (var arm in parts.Where(p => p.name == "Forearm_0"))
                Accessory(arm, "Overtime wristband", l.wristbands, new Vector2(0.62f, 0.24f), new Vector2(0f, 0f), 1, null, toward: arm.transform.Find("Hand_0"));
            foreach (var calf in parts.Where(p => p.name == "Calf_0"))
                Accessory(calf, "Overtime knee pad", l.kneePads, new Vector2(0.62f, 0.24f), new Vector2(0f, -0.3f), 3, null);

            keeper.Apply();
        }

        // a coloured band (headband, wristband, knee pad) as a child of a body part, sized from that part's sprite
        private static void Accessory(SpriteRenderer part, string name, int color, Vector2 size, Vector2 offset, int order, SpriteRenderer below, Transform toward = null)
        {
            if (part == null) return;
            var existing = part.transform.Find(name);
            if (color == 0) { if (existing != null) existing.gameObject.SetActive(false); return; }
            var go = existing != null ? existing.gameObject : new GameObject(name);
            go.SetActive(true);
            go.transform.SetParent(part.transform, false);
            var sr = go.GetComponent<SpriteRenderer>() ?? go.AddComponent<SpriteRenderer>();
            sr.sprite = Patches.ContinueMenu.Rounded();
            sr.drawMode = SpriteDrawMode.Sliced;
            var b = part.sprite != null ? part.sprite.bounds.size : Vector3.one;
            var c = part.sprite != null ? part.sprite.bounds.center : Vector3.zero;
            if (toward != null)
            {
                // across the forearm, a little before the hand
                var dir = toward.localPosition;
                float len = dir.magnitude;
                go.transform.localPosition = (Vector3)(Vector2)(dir * 0.78f);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                sr.size = new Vector2(Mathf.Max(0.05f, len * size.y), Mathf.Min(b.x, b.y) * size.x);
            }
            else
            {
                go.transform.localPosition = new Vector3(c.x + offset.x * b.x, c.y + offset.y * b.y, 0f);
                go.transform.localRotation = Quaternion.identity;
                sr.size = new Vector2(b.x * size.x, b.y * size.y);
            }
            sr.color = Vfx.Hex(UniformColors[color].hex);
            sr.sortingLayerID = part.sortingLayerID;
            sr.sortingOrder = part.sortingOrder + order;
            if (below != null && below.sortingOrder <= sr.sortingOrder) below.sortingOrder = sr.sortingOrder + 1;   // fringe over the headband
        }

        private static readonly Dictionary<string, SpriteRenderer> donors = new Dictionary<string, SpriteRenderer>();
        private static SpriteRenderer HairDonor(string sprite)
        {
            if (donors.TryGetValue(sprite, out var d) && d != null) return d;
            d = Resources.FindObjectsOfTypeAll<SpriteRenderer>().FirstOrDefault(r => r.name == "Hair_0" && r.sprite != null && r.sprite.name == sprite &&
                                                                                      r.transform.parent != null && r.transform.parent.name == "Head_0");
            if (d != null) donors[sprite] = d; else Plugin.Log.LogWarning("No game character wears " + sprite);
            return d;
        }

        // A recolourable copy of a sprite that is drawn in colour: its brightness becomes white..grey, so a tint shows.
        private static readonly Dictionary<Sprite, Sprite> tintable = new Dictionary<Sprite, Sprite>();
        private static Sprite Tintable(Sprite src)
        {
            if (src == null) return null;
            if (tintable.ContainsValue(src)) return src;
            if (tintable.TryGetValue(src, out var done) && done != null) return done;
            var r = src.textureRect;
            int tw = src.texture.width, th = src.texture.height;
            // copy the whole (unreadable) texture through a render texture, then cut the sprite's area out of it in
            // texture coordinates (origin bottom-left, like textureRect) - no manual y-flip that can pick the wrong strip
            var rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src.texture, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var full = new Texture2D(tw, th, TextureFormat.RGBA32, false);
            full.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
            full.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            int rx = Mathf.RoundToInt(r.x), ry = Mathf.RoundToInt(r.y), rw = Mathf.RoundToInt(r.width), rh = Mathf.RoundToInt(r.height);
            var tex = new Texture2D(rw, rh, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontUnloadUnusedAsset, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(full.GetPixels(rx, ry, rw, rh));
            tex.Apply();
            if (Plugin.DumpScenes.Value)
            {
                Plugin.Log.LogInfo($"Tintable {src.name}: texture {src.texture.name} {tw}x{th}, textureRect {r}, rect {src.rect}, packed {src.packed}, pivot {src.pivot}, ppu {src.pixelsPerUnit}");
                var dir = System.IO.Path.Combine(Application.persistentDataPath, "overtime_debug");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "full.png"), full.EncodeToPNG());
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "cut.png"), tex.EncodeToPNG());
            }
            UnityEngine.Object.Destroy(full);
            var px = tex.GetPixels();
            float max = 0.01f;
            foreach (var p in px) if (p.a > 0.5f) max = Mathf.Max(max, p.grayscale);
            for (int i = 0; i < px.Length; i++)
            {
                float g = Mathf.Clamp01(px[i].grayscale / max);
                g = 0.35f + 0.65f * g;                           // outlines stay darker than the fill
                px[i] = new Color(g, g, g, px[i].a);
            }
            tex.SetPixels(px);
            tex.Apply();
            var pivot = new Vector2(src.pivot.x / src.rect.width, src.pivot.y / src.rect.height);
            var copy = Sprite.Create(tex, new Rect(0, 0, rw, rh), pivot, src.pixelsPerUnit);
            copy.hideFlags = HideFlags.DontUnloadUnusedAsset;
            copy.name = src.name + " (tintable)";
            tintable[src] = copy;
            return copy;
        }

        // ------------------------------------------------------------ where the player's character is

        // The left team's human player in a match or on the title screen.
        public static bool IsPlayerOne(PlayerController pc) => pc != null && pc.name == "Player" && pc.transform.position.x < 0f;

        public static Transform RigOf(Component c)
        {
            var t = c.transform.Find("Anime sprite");
            return t != null ? t : c.transform;
        }

        // Upgrade / win / lose screens draw the player as a separate character wearing number 13.
        public static void ApplyToSceneCharacters()
        {
            foreach (var num in UnityEngine.Object.FindObjectsByType<TextMeshPro>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!num.name.StartsWith("Number") || num.GetComponentInParent<PlayerController>() != null) continue;
                if (num.text.Trim() != "13" && num.GetComponentInParent<LookKeeper>() == null) continue;
                var core = num.transform.parent;
                if (core == null || core.parent == null) continue;
                Apply(core.parent, Current);
                if (Plugin.DumpScenes.Value)
                    Plugin.Log.LogInfo("Look on scene character " + core.parent.name + ": " + string.Join(", ", core.parent.GetComponentsInChildren<SpriteRenderer>(true).Select(r => r.name + "=" + (r.sprite != null ? r.sprite.name : "-"))));
            }
        }
    }

    // Remembers a rig's original colours/sprites (so looks can be switched back and forth) and keeps the body
    // proportions on top of whatever scale the animation sets.
    internal class LookKeeper : MonoBehaviour
    {
        private readonly Dictionary<SpriteRenderer, (Color c, Sprite s, int order, Vector3 pos, Quaternion rot, Vector3 scale)> orig =
            new Dictionary<SpriteRenderer, (Color, Sprite, int, Vector3, Quaternion, Vector3)>();
        private readonly Dictionary<TextMeshPro, (string text, Color c)> texts = new Dictionary<TextMeshPro, (string, Color)>();
        public Vector2 Build = Vector2.one;
        private Transform core;
        private Vector3 coreBase, coreSet;

        public void Remember()
        {
            foreach (var r in GetComponentsInChildren<SpriteRenderer>(true))
                if (!orig.ContainsKey(r) && !r.name.StartsWith("Overtime"))
                    orig[r] = (r.color, r.sprite, r.sortingOrder, r.transform.localPosition, r.transform.localRotation, r.transform.localScale);
            foreach (var t in GetComponentsInChildren<TextMeshPro>(true))
                if (!texts.ContainsKey(t)) texts[t] = (t.text, t.color);
            if (core == null) { core = transform.Find("Core_0"); if (core != null) coreSet = coreBase = core.localScale; }
        }

        public Color Original(SpriteRenderer r) => orig.TryGetValue(r, out var o) ? o.c : r.color;

        public void Restore()
        {
            foreach (var kv in orig)
                if (kv.Key != null)
                {
                    kv.Key.color = kv.Value.c; kv.Key.sprite = kv.Value.s; kv.Key.sortingOrder = kv.Value.order;
                    if (kv.Key.name == "Hair_0") { kv.Key.transform.localPosition = kv.Value.pos; kv.Key.transform.localRotation = kv.Value.rot; kv.Key.transform.localScale = kv.Value.scale; }
                }
            foreach (var kv in texts) if (kv.Key != null) { kv.Key.text = kv.Value.text; kv.Key.color = kv.Value.c; }
        }

        public void Apply()
        {
            GetComponentInParent<ControllerFx>()?.RefreshColors();
            LateUpdate();
        }

        // put the body scale back to what the animation set (before copying the rig)
        public void SnapBuild()
        {
            if (core == null) return;
            Build = Vector2.one;
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (core == null) return;
            // the animation may set the core's scale every frame; scale relative to whatever it set
            if (core.localScale != coreSet) coreBase = core.localScale;
            coreSet = new Vector3(coreBase.x * Build.x, coreBase.y * Build.y, coreBase.z);
            core.localScale = coreSet;
        }
    }
}

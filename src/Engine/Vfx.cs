using System.Collections;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Generated;
using HarmonyLib;
using UnityEngine;

namespace HangtimeOvertime.Engine
{
    // Visual and feel effects. Reuses the game's own pieces where it can (the ball's trail and hit
    // particles, camera shake/zoom, the title-card slide, the sound library) and builds simple
    // sprites (soft circle, ring, white square) for auras, rings, flashes and ground marks.
    internal static class Vfx
    {
        private static readonly AccessTools.FieldRef<BallMovement, TrailRenderer> fTrail = AccessTools.FieldRefAccess<BallMovement, TrailRenderer>("myTrail");
        private static readonly AccessTools.FieldRef<BallMovement, SpriteRenderer> fBallSprite = AccessTools.FieldRefAccess<BallMovement, SpriteRenderer>("ballSprite");
        private static readonly AccessTools.FieldRef<BallMovement, SpriteRenderer> fSquash = AccessTools.FieldRefAccess<BallMovement, SpriteRenderer>("squashSprite");
        private static readonly AccessTools.FieldRef<BallMovement, ParticleSystem> fHit = AccessTools.FieldRefAccess<BallMovement, ParticleSystem>("hitEffect");
        private static readonly AccessTools.FieldRef<BallMovement, SoundLibrary> fSounds = AccessTools.FieldRefAccess<BallMovement, SoundLibrary>("soundLibrary");

        public static float SlowmoUntil, SlowmoScale = 1f;

        private static Sprite square, soft, ring;
        private static Runner runner;

        // trail / ball defaults, captured once per ball
        private static BallMovement capturedFor;
        private static Gradient trailGradient;
        private static float trailWidth;
        private static Color ballColor, squashColor;
        private static float tintUntil, trailUntil;

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.white;
        public static string ToHex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        private class Runner : MonoBehaviour
        {
            private void Update()
            {
                var ball = GameManager.Instance != null && GameManager.Instance.ball != null ? GameManager.Instance.ball.GetComponent<BallMovement>() : null;
                if (ball == null || capturedFor != ball) return;
                if (tintUntil > 0f && Time.time > tintUntil) { tintUntil = 0f; fBallSprite(ball).color = ballColor; }
                if (trailUntil > 0f && Time.time > trailUntil) { trailUntil = 0f; RestoreTrail(ball); }
            }
        }

        private static Runner R
        {
            get
            {
                if (runner == null)
                {
                    var go = new GameObject("OvertimeVfx");
                    Object.DontDestroyOnLoad(go);
                    runner = go.AddComponent<Runner>();
                    go.AddComponent<Fakeout>();
                }
                return runner;
            }
        }

        public static void Init() { _ = R; }

        // ------------------------------------------------------------ procedural sprites

        private static Sprite MakeSprite(int size, System.Func<float, float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(dx, dy)) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            var s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            s.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return s;
        }

        private static Sprite Square => square ??= MakeSprite(4, (x, y) => 1f);
        private static Sprite Soft => soft ??= MakeSprite(64, (x, y) => { float d = Mathf.Sqrt(x * x + y * y); return (1f - d) * (1f - d) * 1.2f; });
        private static Sprite Ring => ring ??= MakeSprite(128, (x, y) => { float d = Mathf.Sqrt(x * x + y * y); return 1f - Mathf.Abs(d - 0.85f) / 0.12f; });

        private static SpriteRenderer MakeRenderer(string name, Sprite sprite, Color color, Vector3 pos, SpriteRenderer sortLike, int orderOffset)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            if (sortLike != null) { sr.sortingLayerID = sortLike.sortingLayerID; sr.sortingOrder = sortLike.sortingOrder + orderOffset; }
            return sr;
        }

        private static BallMovement Ball => GameManager.Instance != null && GameManager.Instance.ball != null ? GameManager.Instance.ball.GetComponent<BallMovement>() : null;
        private static SpriteRenderer BallSort => Ball != null ? fBallSprite(Ball) : null;

        // ------------------------------------------------------------ dispatch

        public static void Run(FxDef fx, FxCtx ctx)
        {
            Vector3 At(string at)
            {
                if (at == "owner" && ctx.Source != null) return ctx.Source.transform.position + Vector3.up * 1.5f;
                if (at == "enemy")
                {
                    var e = Engine.Team(Engine.Other(ctx.Side)).OrderBy(c => Mathf.Abs(c.transform.position.x)).FirstOrDefault();
                    if (e != null) return e.transform.position + Vector3.up * 1.5f;
                }
                var b = ctx.Ball != null ? ctx.Ball : Ball;
                return b != null ? b.transform.position : Vector3.zero;
            }
            switch (fx.Kind)
            {
                case "trail": Trail(Hex(fx.S1), fx.A, fx.B); break;
                case "tint": Tint(Hex(fx.S1), fx.A); break;
                case "burst": Burst(Hex(fx.S1), fx.A, At(fx.S2)); break;
                case "flash": Flash(Hex(fx.S1), fx.A, fx.B); break;
                case "slowmo": Slowmo(fx.A, fx.B); break;
                case "shake": Shake(fx.A); break;
                case "zoom": Zoom(fx.A); break;
                case "cutin": CutIn(fx.S1); break;
                case "sound": Sound(fx.S1, fx.A, fx.B); break;
                case "aura": Aura(ctx, Hex(fx.S1), fx.A); break;
                case "afterimages": Afterimages(ctx, Hex(fx.S1), fx.A); break;
                case "bolt": Bolt(Hex(fx.S1), At(fx.S2)); break;
                case "ring": RingFx(Hex(fx.S1), fx.A, At(fx.S2)); break;
            }
        }

        // ------------------------------------------------------------ ball look

        private static void Capture(BallMovement ball)
        {
            if (capturedFor == ball) return;
            capturedFor = ball;
            var t = fTrail(ball);
            trailGradient = t.colorGradient;
            trailWidth = t.widthMultiplier;
            ballColor = fBallSprite(ball).color;
            squashColor = fSquash(ball).color;
        }

        private static void RestoreTrail(BallMovement ball)
        {
            var t = fTrail(ball);
            t.colorGradient = trailGradient;
            t.widthMultiplier = trailWidth;
        }

        public static void ResetBall(BallMovement ball)
        {
            if (ball == null) return;
            Capture(ball);
            RestoreTrail(ball);
            fBallSprite(ball).color = ballColor;
            fSquash(ball).color = squashColor;
            tintUntil = trailUntil = 0f;
        }

        public static void Trail(Color c, float width, float dur)
        {
            var ball = Ball;
            if (ball == null) return;
            Capture(ball);
            var t = fTrail(ball);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(Color.Lerp(c, Color.white, 0.3f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            t.colorGradient = g;
            t.widthMultiplier = trailWidth * width;
            trailUntil = Time.time + dur;
        }

        public static void Tint(Color c, float dur)
        {
            var ball = Ball;
            if (ball == null) return;
            Capture(ball);
            fBallSprite(ball).color = c;
            tintUntil = Time.time + dur;
        }

        public static void BallAlpha(BallMovement ball, float a)
        {
            if (ball == null) return;
            Capture(ball);
            var s = fBallSprite(ball); var c = s.color; c.a = a * ballColor.a; s.color = c;
            var q = fSquash(ball); var c2 = q.color; c2.a = a * squashColor.a; q.color = c2;
            var t = fTrail(ball);
            if (a < 1f) t.widthMultiplier = trailWidth * a; else if (trailUntil == 0f) t.widthMultiplier = trailWidth;
        }

        public static GameObject MakeDecoy(BallMovement ball)
        {
            var src = fBallSprite(ball);
            if (src == null) return null;
            var sr = MakeRenderer("Overtime decoy", src.sprite, src.color, ball.transform.position, src, -1);
            sr.transform.localScale = src.transform.lossyScale;
            return sr.gameObject;
        }

        // ------------------------------------------------------------ bursts, flashes, rings

        public static void Burst(Color c, float size, Vector3 pos)
        {
            var ball = Ball;
            var src = ball != null ? fHit(ball) : null;
            if (src == null) return;
            var go = Object.Instantiate(src.gameObject, pos, Quaternion.identity);
            go.transform.localScale = src.transform.lossyScale * Mathf.Max(0.2f, size);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(c, Color.Lerp(c, Color.white, 0.5f));
                ps.Play();
            }
            Object.Destroy(go, 2.5f);
        }

        public static void Flash(Color c, float alpha, float dur)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var top = SortingLayer.layers.Length > 0 ? SortingLayer.layers[SortingLayer.layers.Length - 1].id : 0;
            var sr = MakeRenderer("Overtime flash", Square, new Color(c.r, c.g, c.b, alpha), cam.transform.position + Vector3.forward, null, 0);
            sr.sortingLayerID = top;
            sr.sortingOrder = 32000;
            sr.transform.SetParent(cam.transform, true);
            float h = cam.orthographicSize * 2.4f;
            sr.transform.localScale = new Vector3(h * cam.aspect + 4f, h, 1f);
            R.StartCoroutine(Fade(sr, alpha, dur, true));
        }

        private static IEnumerator Fade(SpriteRenderer sr, float from, float dur, bool unscaled, float grow = 0f)
        {
            float t = 0f;
            var baseScale = sr.transform.localScale;
            while (t < dur && sr != null)
            {
                t += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
                var c = sr.color; c.a = from * (1f - t / dur); sr.color = c;
                if (grow > 0f) sr.transform.localScale = baseScale * (1f + grow * t / dur);
                yield return null;
            }
            if (sr != null) Object.Destroy(sr.gameObject);
        }

        public static void RingFx(Color c, float size, Vector3 pos)
        {
            var sr = MakeRenderer("Overtime ring", Ring, new Color(c.r, c.g, c.b, 0.9f), pos, BallSort, 3);
            sr.transform.localScale = Vector3.one * 1.5f;
            R.StartCoroutine(Fade(sr, 0.9f, 0.4f, false, Mathf.Max(1f, size * 3f)));
        }

        public static GameObject GroundMark(Color c, float x, float width, float dur)
        {
            var sr = MakeRenderer("Overtime ground mark", Soft, new Color(c.r, c.g, c.b, 0.6f), new Vector3(x, -9.4f, 0f), BallSort, -2);
            sr.transform.localScale = new Vector3(width, 1.6f, 1f);
            R.StartCoroutine(Fade(sr, 0.6f, dur, false));
            return sr.gameObject;
        }

        public static void Bolt(Color c, Vector3 target)
        {
            var ball = Ball;
            var trail = ball != null ? fTrail(ball) : null;
            var go = new GameObject("Overtime bolt");
            var lr = go.AddComponent<LineRenderer>();
            if (trail != null) lr.sharedMaterial = trail.sharedMaterial;
            var sort = BallSort;
            if (sort != null) { lr.sortingLayerID = sort.sortingLayerID; lr.sortingOrder = sort.sortingOrder + 4; }
            const int n = 8;
            lr.positionCount = n;
            var top = target + new Vector3(Random.Range(-3f, 3f), 16f, 0f);
            for (int i = 0; i < n; i++)
            {
                var p = Vector3.Lerp(top, target, i / (float)(n - 1));
                if (i > 0 && i < n - 1) p += new Vector3(Random.Range(-1.2f, 1.2f), 0f, 0f);
                lr.SetPosition(i, p);
            }
            lr.startWidth = 0.45f; lr.endWidth = 0.2f;
            lr.startColor = Color.white; lr.endColor = c;
            R.StartCoroutine(BoltLife(lr, c));
        }

        private static IEnumerator BoltLife(LineRenderer lr, Color c)
        {
            for (float t = 0f; t < 0.3f && lr != null; t += Time.unscaledDeltaTime)
            {
                bool on = Mathf.Repeat(t * 30f, 1f) < 0.7f;
                lr.startColor = on ? Color.white : c; lr.endColor = on ? c : Color.white;
                yield return null;
            }
            if (lr != null) Object.Destroy(lr.gameObject);
        }

        // ------------------------------------------------------------ players

        public static GameObject MakeAura(Transform player, SpriteRenderer sortLike)
        {
            var sr = MakeRenderer("Overtime aura", Soft, Color.white, player.position + Vector3.up * 1.4f, sortLike, -30);
            sr.transform.SetParent(player, true);
            sr.transform.localScale = Vector3.one * 6.5f;
            return sr.gameObject;
        }

        private static void Aura(FxCtx ctx, Color c, float dur)
        {
            foreach (var p in Engine.Team(ctx.Side).Where(p => !p.IsSetter)) p.SetAura(c, dur);
        }

        private static void Afterimages(FxCtx ctx, Color c, float dur)
        {
            if (dur == -1f) Engine.Sides[ctx.Side].MatchAfterimages = c;
            foreach (var p in Engine.Team(ctx.Side).Where(p => !p.IsSetter)) p.SetAfterimages(c, dur);
        }

        public static void Ghost(SpriteRenderer[] parts, Color c)
        {
            if (parts == null) return;
            foreach (var p in parts)
            {
                if (p == null || !p.enabled || p.sprite == null) continue;
                var sr = MakeRenderer("Overtime ghost", p.sprite, new Color(c.r, c.g, c.b, 0.45f), p.transform.position, p, -40);
                sr.flipX = p.flipX;
                sr.transform.rotation = p.transform.rotation;
                sr.transform.localScale = p.transform.lossyScale;
                R.StartCoroutine(Fade(sr, 0.45f, 0.25f, false));
            }
        }

        // ------------------------------------------------------------ camera, time, sound, callouts

        public static void Slowmo(float scale, float dur)
        {
            SlowmoScale = Mathf.Clamp(scale, 0.1f, 1f);
            SlowmoUntil = Time.unscaledTime + dur;
        }

        public static void Shake(float amount) => CameraController.Shake(amount);

        public static void Zoom(float amount) => CameraController.zoom(amount);

        public static void CutIn(string text)
        {
            if (TitleCard.instance != null) TitleCard.instance.TextSlide(text);
        }

        public static void Sound(string clip, float vol, float pitch)
        {
            var ball = Ball;
            var lib = ball != null ? fSounds(ball) : null;
            if (lib == null || AudioManager.Instance == null) return;
            var ac = Traverse.Create(lib).Field(clip).GetValue<AudioClip>();
            if (ac != null) AudioManager.Instance.PlaySFX(ac, vol, 0.05f, pitch);
        }
    }
}

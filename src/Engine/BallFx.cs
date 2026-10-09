using System.Collections.Generic;
using HangtimeOvertime.Generated;
using HarmonyLib;
using UnityEngine;

namespace HangtimeOvertime.Engine
{
    // The ball's current flight: who sent it, effects waiting for it to cross the net or for the
    // other team's first touch, and effects running on it each physics step. Any accepted touch,
    // serve or block starts a new flight; the end of a rally clears everything.
    internal static class BallFx
    {

        private class Flight
        {
            public int Owner = -1;
            public bool Crossed;
            public readonly List<(FxDef fx, FxCtx ctx)> OnCross = new List<(FxDef, FxCtx)>();
            public readonly List<(FxDef fx, FxCtx ctx)> OnEnemyTouch = new List<(FxDef, FxCtx)>();
        }

        private abstract class Running
        {
            public float Until;
            public abstract void Step(Rigidbody2D rb, float dt);
            public virtual void End(Rigidbody2D rb, bool interrupted) { }
        }

        private static Flight flight = new Flight();
        private static readonly List<Running> running = new List<Running>();
        private static BallMovement ballRef;

        public static int Owner => flight.Owner;

        // ------------------------------------------------------------ flights

        public static void NewFlight(int owner, BallMovement ball)
        {
            ballRef = ball;
            EndRunning(true);
            Vfx.ResetBall(ball);
            flight = new Flight { Owner = owner };
        }

        public static void Clear()
        {
            EndRunning(true);
            if (ballRef != null) Vfx.ResetBall(ballRef);
            flight = new Flight();
        }

        public static void Queue(FxDef fx, FxCtx ctx)
        {
            if (fx.When == "cross") flight.OnCross.Add((fx, ctx));
            else if (fx.When == "enemy_touch") flight.OnEnemyTouch.Add((fx, ctx));
        }

        // Called by the touch patches before a new flight replaces this one.
        public static void EnemyTouched(int toucherSide, BallMovement ball)
        {
            if (flight.Owner < 0 || toucherSide == flight.Owner || !flight.Crossed) return;
            foreach (var (fx, ctx) in flight.OnEnemyTouch) Engine.Run(fx, ctx.WithBall(ball));
            flight.OnEnemyTouch.Clear();
        }

        public static void Step(BallMovement ball)
        {
            ballRef = ball;
            var rb = ball.GetComponent<Rigidbody2D>();
            if (BallMovement.rallyDone) { if (flight.Owner >= 0 || running.Count > 0) Clear(); return; }
            if (flight.Owner >= 0 && !flight.Crossed)
            {
                float x = ball.transform.position.x;
                if (flight.Owner == 0 ? x > 0f : x < 0f)
                {
                    flight.Crossed = true;
                    foreach (var (fx, ctx) in flight.OnCross) Engine.Run(fx, ctx.WithBall(ball));
                    flight.OnCross.Clear();
                }
            }
            float dt = Time.fixedDeltaTime;
            for (int i = running.Count - 1; i >= 0; i--)
            {
                var r = running[i];
                if (Time.time > r.Until) { r.End(rb, false); running.RemoveAt(i); continue; }
                r.Step(rb, dt);
            }
        }

        private static void EndRunning(bool interrupted)
        {
            var rb = ballRef != null ? ballRef.GetComponent<Rigidbody2D>() : null;
            foreach (var r in running) if (rb != null) r.End(rb, interrupted);
            running.Clear();
            Fakeout.Offset = 0f;
        }

        private static float TowardEnemy(int owner) => owner == 0 ? 1f : -1f;

        // ------------------------------------------------------------ effects

        public static void Apply(FxDef fx, FxCtx ctx)
        {
            var ball = ctx.Ball != null ? ctx.Ball : ballRef;
            if (ball == null) return;
            var rb = ball.GetComponent<Rigidbody2D>();
            switch (fx.Kind)
            {
                case "speed": Speed(rb, fx.A); break;
                case "accel": Accel(fx.A, fx.B); break;
                case "gravity": Gravity(fx.A, fx.B); break;
                case "plunge": Plunge(fx.A, fx.B, fx.C); break;
                case "curve": Curve(fx.A, fx.B, ctx.Side); break;
                case "wobble": Wobble(fx.A, fx.B, fx.C, fx.S1); break;
                case "hover": Hover(rb, fx.A, fx.B); break;
                case "slow": Slow(rb, fx.A, fx.B); break;
                case "gust": Gust(rb, fx.A, fx.B); break;
                case "lift": Lift(rb, fx.A); break;
                case "deflect": Deflect(rb, fx.A, fx.B, fx.C, ctx.Side); break;
                case "invisible": Invisible(ball, fx.A, fx.B); break;
                case "decoy": Decoy(ball, fx.A, fx.B, fx.C > 0f, ctx.Side); break;
            }
            Plugin.Trace($"ball {fx.Kind} -> v={rb.linearVelocity}");
        }

        public static void Speed(Rigidbody2D rb, float mult) => rb.linearVelocity *= mult;

        public static void Lift(Rigidbody2D rb, float vy) => rb.linearVelocity += new Vector2(0f, vy);

        public static void Gust(Rigidbody2D rb, float min, float max) => rb.linearVelocity += new Vector2(0f, Random.Range(min, max));

        // After the other team's touch (side = the perk owner): sideways kick, push toward the toucher's back line, height cut.
        public static void Deflect(Rigidbody2D rb, float rand, float back, float ymult, int owner)
        {
            float toBack = TowardEnemy(owner);   // the toucher stands on the owner's enemy side; their back line is further that way
            rb.linearVelocity = new Vector2(rb.linearVelocity.x + Random.Range(-rand, rand) + back * toBack, rb.linearVelocity.y * ymult);
        }

        private class AccelRun : Running
        {
            public float Rate;
            public override void Step(Rigidbody2D rb, float dt) => rb.linearVelocity *= 1f + Rate * dt;
            public override void End(Rigidbody2D rb, bool interrupted) { if (!interrupted) Plugin.Trace($"accel done: speed {rb.linearVelocity.magnitude:F1}"); }
        }

        public static void Accel(float rate, float dur) => running.Add(new AccelRun { Rate = rate, Until = Time.time + dur });

        // Added after the game's own gravity each step (BallMovement.SetGravity runs first).
        private class GravityRun : Running
        {
            public float Add;
            public override void Step(Rigidbody2D rb, float dt) => rb.gravityScale += Add;
        }

        public static void Gravity(float add, float dur) => running.Add(new GravityRun { Add = add, Until = Time.time + dur });

        private class PlungeRun : Running
        {
            public float Vy, XMult, At;
            public bool Done;
            public override void Step(Rigidbody2D rb, float dt)
            {
                if (Done || Time.time < At) return;
                Done = true;
                rb.linearVelocity = new Vector2(rb.linearVelocity.x * XMult, Mathf.Min(rb.linearVelocity.y, -Mathf.Abs(Vy)));
                Plugin.Trace($"plunge -> {rb.linearVelocity}");
            }
        }

        public static void Plunge(float vy, float xmult, float delay) =>
            running.Add(new PlungeRun { Vy = vy, XMult = xmult, At = Time.time + delay, Until = Time.time + delay + 0.1f });

        private class CurveRun : Running
        {
            public float Ax;
            public override void Step(Rigidbody2D rb, float dt) => rb.linearVelocity += new Vector2(Ax * dt, 0f);
        }

        public static void Curve(float ax, float dur, int owner) => running.Add(new CurveRun { Ax = ax * TowardEnemy(owner), Until = Time.time + dur });

        private class WobbleRun : Running
        {
            public float Amp, Period, Start, Sign = 1f, NextFlip;
            public bool Zigzag;
            public override void Step(Rigidbody2D rb, float dt)
            {
                float t = Time.time - Start;
                if (Zigzag)
                {
                    if (Time.time >= NextFlip)
                    {
                        rb.linearVelocity += new Vector2(0f, Amp * Sign * (NextFlip == Start ? 1f : 2f));
                        Sign = -Sign;
                        NextFlip += Period;
                    }
                }
                else rb.linearVelocity += new Vector2(0f, Amp * 6f * Mathf.Sin(2f * Mathf.PI * t / Period) * dt);
            }
        }

        public static void Wobble(float amp, float period, float dur, string mode)
        {
            float now = Time.time;
            running.Add(new WobbleRun { Amp = amp, Period = Mathf.Max(0.04f, period), Start = now, NextFlip = now, Zigzag = mode == "zigzag", Until = now + dur });
        }

        private class HoverRun : Running
        {
            public Vector2 Saved;
            public float Then;
            public override void Step(Rigidbody2D rb, float dt)
            {
                rb.linearVelocity = Saved.normalized * 0.01f;
                rb.gravityScale = 0f;
            }
            public override void End(Rigidbody2D rb, bool interrupted) { if (!interrupted) rb.linearVelocity = Saved * Then; }
        }

        public static void Hover(Rigidbody2D rb, float dur, float then) =>
            running.Add(new HoverRun { Saved = rb.linearVelocity, Then = then, Until = Time.time + dur });

        private class SlowRun : Running
        {
            public float Mult;
            public override void Step(Rigidbody2D rb, float dt) { }
            public override void End(Rigidbody2D rb, bool interrupted) { if (!interrupted) rb.linearVelocity /= Mult; }
        }

        public static void Slow(Rigidbody2D rb, float mult, float dur)
        {
            mult = Mathf.Clamp(mult, 0.2f, 1f);
            rb.linearVelocity *= mult;
            running.Add(new SlowRun { Mult = mult, Until = Time.time + dur });
        }

        private class InvisibleRun : Running
        {
            public BallMovement Ball;
            public float Alpha;
            public override void Step(Rigidbody2D rb, float dt) => Vfx.BallAlpha(Ball, Alpha);
            public override void End(Rigidbody2D rb, bool interrupted) => Vfx.BallAlpha(Ball, 1f);
        }

        public static void Invisible(BallMovement ball, float alpha, float dur) =>
            running.Add(new InvisibleRun { Ball = ball, Alpha = Mathf.Clamp01(alpha), Until = Time.time + dur });

        private class DecoyRun : Running
        {
            public GameObject Go;
            public Vector2 Pos, Vel;
            public float Gravity, Born, Life;
            public override void Step(Rigidbody2D rb, float dt)
            {
                if (Go == null) return;
                Vel += new Vector2(0f, Gravity * dt);
                Pos += Vel * dt;
                Go.transform.position = Pos;
                var sr = Go.GetComponent<SpriteRenderer>();
                var c = sr.color; c.a = Mathf.Clamp01(1f - (Time.time - Born) / Life) * 0.85f; sr.color = c;
            }
            public override void End(Rigidbody2D rb, bool interrupted)
            {
                if (Go != null) Object.Destroy(Go);
                Fakeout.Offset = 0f;
            }
        }

        public static void Decoy(BallMovement ball, float offset, float dur, bool fake, int owner)
        {
            var rb = ball.GetComponent<Rigidbody2D>();
            float dir = Random.value < 0.5f ? -1f : 1f;
            var go = Vfx.MakeDecoy(ball);
            if (go == null) return;
            var run = new DecoyRun
            {
                Go = go, Pos = ball.transform.position, Vel = rb.linearVelocity + new Vector2(dir * offset * 2.5f, 4f),
                Gravity = Physics2D.gravity.y * rb.gravityScale, Born = Time.time, Life = dur + 0.25f, Until = Time.time + dur + 0.25f,
            };
            running.Add(run);
            if (fake) Fakeout.Offset = dir * offset;
        }
    }

    // While a decoy is out, the enemy AI reads a landing spot shifted by Offset. Written in LateUpdate,
    // after the game's own prediction coroutine has refreshed the value for this frame.
    internal class Fakeout : MonoBehaviour
    {
        public static float Offset;
        private static float lastWritten = float.NaN, lastWrittenPrecise = float.NaN;

        private void LateUpdate()
        {
            if (Offset == 0f) { lastWritten = lastWrittenPrecise = float.NaN; return; }
            if (BallMovement.predictedPosition != lastWritten) BallMovement.predictedPosition += Offset;
            if (BallMovement.precisePredictedPosition != lastWrittenPrecise) BallMovement.precisePredictedPosition += Offset;
            lastWritten = BallMovement.predictedPosition;
            lastWrittenPrecise = BallMovement.precisePredictedPosition;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using HangtimeOvertime.Core;
using HangtimeOvertime.Generated;
using HarmonyLib;
using UnityEngine;

namespace HangtimeOvertime.Engine
{
    // Lives on every PlayerController (both teams). Owns the player's stat fields after the game
    // set them: base values (+ Overtime stat cards for the player's team) times active buffs,
    // slows and ground zones; plus stuns, landing recovery, auras and afterimages.
    public class ControllerFx : MonoBehaviour
    {
        public static readonly List<ControllerFx> All = new List<ControllerFx>();

        private static readonly AccessTools.FieldRef<PlayerController, float> fSpike = AccessTools.FieldRefAccess<PlayerController, float>("spike");
        private static readonly AccessTools.FieldRef<PlayerController, float> fJump = AccessTools.FieldRefAccess<PlayerController, float>("jump");
        private static readonly AccessTools.FieldRef<PlayerController, float> fBlock = AccessTools.FieldRefAccess<PlayerController, float>("block");
        private static readonly AccessTools.FieldRef<PlayerController, float> fBump = AccessTools.FieldRefAccess<PlayerController, float>("bump");
        private static readonly AccessTools.FieldRef<PlayerController, float> fSpin = AccessTools.FieldRefAccess<PlayerController, float>("spinServe");
        private static readonly AccessTools.FieldRef<PlayerController, float> fServeJump = AccessTools.FieldRefAccess<PlayerController, float>("serveJump");
        private static readonly AccessTools.FieldRef<PlayerController, float> fFloat = AccessTools.FieldRefAccess<PlayerController, float>("floatServe");

        public PlayerController Pc { get; private set; }
        public int Side { get; private set; }
        public bool IsSetter => Pc != null && Pc.setter;
        public bool Ready { get; private set; }

        private Rigidbody2D rb;
        private float baseSpike, baseJump, baseBlock, baseMove;
        private readonly List<(string stat, float mult, float until)> buffs = new List<(string, float, float)>();
        private readonly List<(float mult, float until)> slows = new List<(float, float)>();
        private float stunUntil, lagUntil, burstUntil, airStart, serveStart;
        private bool wasGrounded = true;
        public float LastServeHold { get; private set; }

        public bool Stunned => Time.time < stunUntil;
        public bool Lagging => Time.time < lagUntil;
        public float AirTime => wasGrounded ? 0f : Time.time - airStart;

        // visuals
        private GameObject aura;
        private float auraUntil;
        private Color? afterimageColor;
        private float afterimageUntil, nextGhost;
        private SpriteRenderer[] parts;
        private Color[] partColors;

        private void Awake()
        {
            Pc = GetComponent<PlayerController>();
            rb = GetComponent<Rigidbody2D>();
            All.Add(this);
        }

        private void OnDestroy() => All.Remove(this);

        private IEnumerator Start()
        {
            yield return null;   // the game's setStats runs one frame after Start,
            yield return null;   // technique OnStart right after it
            Side = Engine.SideOf(Pc);
            baseSpike = fSpike(Pc); baseJump = fJump(Pc); baseBlock = fBlock(Pc); baseMove = Pc.moveSpeed;
            if (Side == 0) StatCards.ApplyTo(this);
            foreach (var (stat, mult) in Engine.Sides[Side].MatchBuffs) AddBuff(stat, mult, -1f);
            var anime = transform.Find("Anime sprite");
            parts = (anime != null ? anime : transform).GetComponentsInChildren<SpriteRenderer>(true);
            partColors = System.Array.ConvertAll(parts, p => p.color);
            Ready = true;
            Plugin.Trace($"{name} side {Side} ready: spike {baseSpike:F2} jump {baseJump:F1} block {baseBlock:F1} move {baseMove:F2}");
        }

        // Stat cards add to the base values (game units); called once when this player is ready.
        public void AddBase(string field, float amount)
        {
            switch (field)
            {
                case "Spike": baseSpike = Mathf.Max(baseSpike * 0.6f, baseSpike + amount); break;
                case "Jump": if (!IsSetter) baseJump = Mathf.Max(baseJump * 0.6f, baseJump + amount); break;
                case "Block": baseBlock = Mathf.Max(baseBlock * 0.6f, baseBlock + amount); break;
                case "Bump": fBump(Pc) = Mathf.Max(1f, fBump(Pc) + amount); break;
                case "SpinServe": fSpin(Pc) = Mathf.Max(fSpin(Pc) * 0.6f, fSpin(Pc) + amount); break;
                case "ServeJump": fServeJump(Pc) = Mathf.Max(fServeJump(Pc) * 0.6f, fServeJump(Pc) + amount); break;
                case "FloatServe": fFloat(Pc) = Mathf.Max(fFloat(Pc) * 0.6f, fFloat(Pc) + amount); break;
                case "move": case "setter_move": baseMove = Mathf.Max(baseMove * 0.6f, baseMove + amount); break;
            }
        }

        public float RecoverySteps;   // from stat cards (player team)

        public void AddBuff(string stat, float mult, float dur)
        {
            float until = dur == -1f ? float.MaxValue : dur == -2f ? -2f : Time.time + dur;
            buffs.Add((stat, mult, until));
        }

        public void AddSlow(float mult, float dur) => slows.Add((mult, Time.time + dur));

        public void Stun(float dur)
        {
            stunUntil = Mathf.Max(stunUntil, Time.time + dur);
            Plugin.Trace($"{name} (side {Side}) stunned {dur:F2}s");
        }

        public void EndRally()
        {
            buffs.RemoveAll(b => b.until == -2f);
            if (auraUntil == -2f) SetAura(null, 0f);
        }

        public float ServeStartTime => serveStart;
        public void ServeStarted() => serveStart = Time.time;
        public void Served() => LastServeHold = Time.time - serveStart;

        private float Mult(string stat)
        {
            float m = 1f;
            buffs.RemoveAll(b => b.until > 0f && b.until != float.MaxValue && Time.time > b.until);
            foreach (var b in buffs) if (b.stat == stat) m *= b.mult;
            return m;
        }

        private void FixedUpdate()
        {
            if (!Ready) return;
            bool grounded = Pc.getGrounded();
            if (wasGrounded && !grounded) airStart = Time.time;
            if (!wasGrounded && grounded) Landed();
            wasGrounded = grounded;

            float move = Mult(IsSetter ? "set" : "move");
            slows.RemoveAll(s => Time.time > s.until);
            foreach (var s in slows) move *= s.mult;
            move *= Engine.ZoneMult(this);
            if (Lagging) move *= 0.65f;
            if (Time.time < burstUntil) move *= 1f + 0.1f * RecoverySteps;
            if (Stunned) { move = 0f; rb.linearVelocity = new Vector2(rb.linearVelocity.x * 0.7f, rb.linearVelocity.y); }

            Pc.moveSpeed = baseMove * move;
            fSpike(Pc) = baseSpike * Mult("spike");
            if (!IsSetter) fJump(Pc) = baseJump * Mult("jump");
            fBlock(Pc) = baseBlock * Mult("block");
        }

        private void Landed()
        {
            if (RecoverySteps < 0f) lagUntil = Time.time + -RecoverySteps * Stats.All[System.Array.FindIndex(Stats.All, s => s.Id == "Recovery")].Step;
            else if (RecoverySteps > 0f) burstUntil = Time.time + 0.35f;
        }

        // ------------------------------------------------------------ visuals

        public void SetAura(Color? color, float dur)
        {
            if (color == null) { if (aura != null) Destroy(aura); aura = null; auraUntil = 0f; return; }
            if (aura == null) aura = Vfx.MakeAura(transform, parts != null && parts.Length > 0 ? parts[0] : null);
            aura.GetComponent<SpriteRenderer>().color = new Color(color.Value.r, color.Value.g, color.Value.b, 0.5f);
            auraUntil = dur == -1f ? float.MaxValue : dur == -2f ? -2f : Time.time + dur;
        }

        public void SetAfterimages(Color color, float dur)
        {
            afterimageColor = color;
            afterimageUntil = dur == -1f ? float.MaxValue : Time.time + dur;
        }

        private void Update()
        {
            if (!Ready) return;
            if (aura != null)
            {
                if (auraUntil != -2f && Time.time > auraUntil) SetAura(null, 0f);
                else
                {
                    var sr = aura.GetComponent<SpriteRenderer>();
                    var c = sr.color; c.a = 0.35f + 0.2f * Mathf.Sin(Time.time * 10f); sr.color = c;
                    aura.transform.localScale = Vector3.one * (6.5f + 0.4f * Mathf.Sin(Time.time * 7f));
                }
            }
            if (afterimageColor != null)
            {
                if (Time.time > afterimageUntil) afterimageColor = null;
                else if (Mathf.Abs(rb.linearVelocity.x) > 8f && Time.time > nextGhost)
                {
                    nextGhost = Time.time + 0.06f;
                    Vfx.Ghost(parts, afterimageColor.Value);
                }
            }
            bool flicker = Stunned && Mathf.Repeat(Time.time * 14f, 1f) < 0.5f;
            if (parts != null && flicker != flickering)
            {
                flickering = flicker;
                for (int i = 0; i < parts.Length; i++)
                    if (parts[i] != null) parts[i].color = flicker ? Color.Lerp(partColors[i], new Color(1f, 0.95f, 0.3f), 0.7f) : partColors[i];
            }
        }

        private bool flickering;

        // Team dressing recolors parts after this component read them.
        public void RefreshColors()
        {
            if (parts != null) partColors = System.Array.ConvertAll(parts, p => p.color);
        }
    }
}

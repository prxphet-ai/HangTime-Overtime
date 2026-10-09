using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Generated;
using UnityEngine;

namespace HangtimeOvertime.Engine
{
    // Runs perks for either team: checks a row's conditions, applies element synergy, runs its
    // "now" effects and queues "cross"/"enemy_touch" effects on the ball's current flight.
    // Side 0 is the left team (the player's), side 1 the right team (the opponent).
    internal static class Engine
    {
        internal class SideState
        {
            public int TouchStreak;          // accepted touches in a row without losing a rally
            public int RallyWins;            // rallies won in a row
            public int RalliesLost;          // rallies lost in a row
            public float Bank;
            public float NextPower, NextPowerUntil;
            public float GameSpeed = 1f;
            public readonly Dictionary<string, float> CooldownUntil = new Dictionary<string, float>();
            public readonly HashSet<string> SynergyAnnounced = new HashSet<string>();
            public readonly List<(string stat, float mult)> MatchBuffs = new List<(string, float)>();
            public Color? MatchAfterimages;
            public PlayerController LastSpiker;
        }

        public static readonly SideState[] Sides = { new SideState(), new SideState() };
        public static int RallyTouches;                 // accepted touches by both teams in the current rally
        public static PlayerController CurrentInput;   // set by the UpInputPressed prefix (OnBlockJump has no args)

        private static readonly Dictionary<string, FxKindDef> fxKinds = FxKinds.All.ToDictionary(k => k.Id);
        private static readonly Dictionary<string, ElementDef> elements = Generated.Elements.All.ToDictionary(e => e.Id);

        public static int SideOf(PlayerController pc) => pc.transform.position.x < 0f ? 0 : 1;
        public static int Other(int side) => 1 - side;

        public static void ResetMatch()
        {
            for (int s = 0; s < 2; s++) Sides[s] = new SideState();
            RallyTouches = 0;
            spikePlans.Clear();
            Zones.Clear();
        }

        // ------------------------------------------------------------ ownership

        public static PlayerStats StatsOf(int side)
        {
            var gm = GameManager.Instance;
            if (gm == null) return null;
            if (side == 0) return gm.playerStats;
            return gm.currentTeam != null ? gm.currentTeam.GetComponent<PlayerStats>() : null;
        }

        public static IEnumerable<DataPerk> PerksOf(int side)
        {
            var stats = StatsOf(side);
            if (stats == null || stats.techniques == null) yield break;
            foreach (var t in stats.techniques)
                if (t is DataPerk p) yield return p;
        }

        public static IEnumerable<ControllerFx> Team(int side) => ControllerFx.All.Where(c => c != null && c.Side == side);

        private static float Synergy(int side, DataPerk perk, out ElementDef el)
        {
            el = elements.TryGetValue(perk.Def.Element, out var e) ? e : default;
            if (el.Id == null) return 1f;
            int n = PerksOf(side).Count(p => p.Def.Element == perk.Def.Element);
            return n >= 3 ? el.Syn3 : n >= 2 ? el.Syn2 : 1f;
        }

        // Synergy strengthens the one param the effects sheet marks for that effect.
        private static FxDef Scaled(FxDef fx, float m)
        {
            if (m == 1f || !fxKinds.TryGetValue(fx.Kind, out var k) || k.ScaleSlot < 0) return fx;
            float S(float v, int slot) => slot != k.ScaleSlot ? v : k.ScaleExcess ? 1f + (v - 1f) * m : v * m;
            return new FxDef(fx.On, fx.When, fx.Kind, S(fx.A, 0), S(fx.B, 1), S(fx.C, 2), S(fx.D, 3), fx.S1, fx.S2);
        }

        // ------------------------------------------------------------ conditions

        private static bool Passes(DataPerk perk, string trigger, int side, PlayerController pc)
        {
            var c = perk.Def.Cond;
            var st = Sides[side];
            string key = perk.Def.Id + ":" + trigger;
            if (c.Cooldown > 0f && st.CooldownUntil.TryGetValue(key, out var until) && Time.time < until) return false;
            if (c.Chance < 1f && Random.value > c.Chance) return false;
            var fx = pc != null ? pc.GetComponent<ControllerFx>() : null;
            if (trigger == "spike")
            {
                if (c.MinAir > 0f && (fx == null || fx.AirTime < c.MinAir)) return false;
                if (pc != null && pc.transform.position.y < c.MinHeight) return false;
            }
            if (trigger == "serve" && c.Hold > 0f && (fx == null || fx.LastServeHold < c.Hold)) return false;
            if (trigger == "jump" && c.Run > 0f && (pc == null || Mathf.Abs(pc.GetComponent<Rigidbody2D>().linearVelocityX) < c.Run)) return false;
            return true;
        }

        private static void StartCooldown(DataPerk perk, string trigger, int side)
        {
            if (perk.Def.Cond.Cooldown > 0f) Sides[side].CooldownUntil[perk.Def.Id + ":" + trigger] = Time.time + perk.Def.Cond.Cooldown;
        }

        // ------------------------------------------------------------ firing

        // Fires one perk for one trigger. Returns the summed jump/block-jump bonus (jump triggers).
        public static float FirePerk(DataPerk perk, string trigger, PlayerController pc, BallMovement ball, bool checkConds = true)
        {
            int side = pc != null ? SideOf(pc) : 0;
            if (checkConds && !Passes(perk, trigger, side, pc)) return 0f;
            StartCooldown(perk, trigger, side);
            float m = Synergy(side, perk, out var el);
            AnnounceSynergy(side, el, m);
            Plugin.Trace($"{(side == 0 ? "P" : "O")} {perk.Def.Title} <- {trigger}");
            float bonus = 0f;
            var ctx = new FxCtx(side, pc, ball, perk);
            foreach (var raw in perk.Def.Fx)
            {
                if (raw.On != trigger) continue;
                var fx = Scaled(raw, m);
                if (fx.When == "now") bonus += Run(fx, ctx);
                else BallFx.Queue(fx, ctx);
            }
            return bonus;
        }

        public static void FireAll(string trigger, int side, PlayerController pc, BallMovement ball)
        {
            foreach (var perk in PerksOf(side).ToList())
                if (perk.Listens(trigger)) FirePerk(perk, trigger, pc, ball);
        }

        private static void AnnounceSynergy(int side, ElementDef el, float m)
        {
            if (m <= 1f || el.Id == null || el.Callout == "-" || !Sides[side].SynergyAnnounced.Add(el.Id)) return;
            Vfx.CutIn(el.Callout);
        }

        // ------------------------------------------------------------ spikes (power is asked before the hit)

        private class SpikePlan
        {
            public readonly List<DataPerk> Fired = new List<DataPerk>();
            public readonly Dictionary<DataPerk, float> Power = new Dictionary<DataPerk, float>();
            public float Extra;                 // banked + stored power, paid out once
            public bool ExtraPaid, UsedBank, UsedNext;
        }

        private static readonly Dictionary<PlayerController, SpikePlan> spikePlans = new Dictionary<PlayerController, SpikePlan>();

        public static void PrepareSpike(PlayerController pc)
        {
            int side = SideOf(pc);
            var st = Sides[side];
            var plan = new SpikePlan();
            foreach (var perk in PerksOf(side).ToList())
            {
                if (!perk.Listens("spike") || !Passes(perk, "spike", side, pc)) continue;
                plan.Fired.Add(perk);
                float m = Synergy(side, perk, out _);
                float pw = 0f;
                foreach (var raw in perk.Def.Fx)
                {
                    if (raw.On != "spike" || raw.When != "now") continue;
                    var fx = Scaled(raw, m);
                    switch (fx.Kind)
                    {
                        case "power": pw += fx.A; break;
                        case "power_streak": pw += Mathf.Min(st.RallyWins * fx.A, fx.B); break;
                        case "power_perks": pw += Mathf.Min((StatsOf(side)?.techniques.Count ?? 0) * fx.A, fx.B); break;
                        case "cash_bank": plan.Extra += st.Bank; plan.UsedBank = true; break;
                    }
                }
                if (pw != 0f) plan.Power[perk] = pw;
            }
            if (st.NextPower > 0f && Time.time < st.NextPowerUntil) { plan.Extra += st.NextPower; plan.UsedNext = true; }
            spikePlans[pc] = plan;
        }

        public static float SpikePowerShare(DataPerk perk, PlayerController pc)
        {
            if (pc == null || !spikePlans.TryGetValue(pc, out var plan)) return 0f;
            float share = plan.Power.TryGetValue(perk, out var p) ? p : 0f;
            if (!plan.ExtraPaid) { share += plan.Extra; plan.ExtraPaid = true; }
            return share;
        }

        // After the game's DoSpike: run the fired perks' other effects only if the spike was accepted.
        public static void CommitSpike(PlayerController pc, BallMovement ball, bool accepted)
        {
            if (!spikePlans.TryGetValue(pc, out var plan)) return;
            spikePlans.Remove(pc);
            if (!accepted) return;
            int side = SideOf(pc);
            var st = Sides[side];
            if (plan.UsedBank) st.Bank = 0f;
            if (plan.UsedNext) st.NextPower = 0f;
            st.LastSpiker = pc;
            foreach (var perk in plan.Fired)
            {
                StartCooldown(perk, "spike", side);
                float m = Synergy(side, perk, out var el);
                AnnounceSynergy(side, el, m);
                Plugin.Trace($"{(side == 0 ? "P" : "O")} {perk.Def.Title} <- spike (+{(plan.Power.TryGetValue(perk, out var pw) ? pw : 0f):F2} power)");
                var ctx = new FxCtx(side, pc, ball, perk);
                foreach (var raw in perk.Def.Fx)
                {
                    if (raw.On != "spike") continue;
                    var fx = Scaled(raw, m);
                    if (fx.When == "now") Run(fx, ctx); else BallFx.Queue(fx, ctx);
                }
            }
            if (plan.Extra > 0f) Plugin.Trace($"{(side == 0 ? "P" : "O")} spent {plan.Extra:F2} stored power");
        }

        // ------------------------------------------------------------ rally events (the score itself is never touched)

        public static void OnAcceptedTouch(int side)
        {
            Sides[side].TouchStreak++;
            foreach (var perk in PerksOf(side).ToList())
            {
                if (!perk.Listens("streak") || perk.Def.Cond.N <= 0f) continue;
                if (Sides[side].TouchStreak % (int)perk.Def.Cond.N == 0) FirePerk(perk, "streak", MainPlayer(side), null);
            }
            RallyTouches++;
            for (int s = 0; s < 2; s++)
                foreach (var perk in PerksOf(s).ToList())
                    if (perk.Listens("long_rally") && perk.Def.Cond.N > 0f && RallyTouches == (int)perk.Def.Cond.N)
                        FirePerk(perk, "long_rally", MainPlayer(s), null);
        }

        public static void OnRallyEnd(int winner)
        {
            int loser = Other(winner);
            RallyTouches = 0;
            Sides[winner].RallyWins++;
            Sides[winner].RalliesLost = 0;
            Sides[loser].RallyWins = 0;
            Sides[loser].RalliesLost++;
            Sides[loser].TouchStreak = 0;
            foreach (var c in ControllerFx.All) c?.EndRally();
            BallFx.Clear();
            foreach (var perk in PerksOf(loser).ToList())
                if (perk.Listens("enemy_streak") && perk.Def.Cond.N > 0f && Sides[loser].RalliesLost == (int)perk.Def.Cond.N)
                    FirePerk(perk, "enemy_streak", MainPlayer(loser), null);
            foreach (var perk in PerksOf(winner).ToList())
                if (perk.Listens("win_streak") && perk.Def.Cond.N > 0f && Sides[winner].RallyWins == (int)perk.Def.Cond.N)
                    FirePerk(perk, "win_streak", MainPlayer(winner), null);
            var gm = GameManager.Instance;
            for (int s = 0; s < 2; s++)
            {
                int enemyPts = s == 0 ? gm.opponentPoints : gm.playerPoints;
                int ownPts = s == 0 ? gm.playerPoints : gm.opponentPoints;
                if (enemyPts == gm.matchLength - 1 && ownPts < gm.matchLength && enemyPts < gm.matchLength)
                    FireAll("match_point_against", s, MainPlayer(s), null);
            }
        }

        public static void FirePassives()
        {
            for (int s = 0; s < 2; s++) FireAll("passive", s, null, null);
        }

        public static PlayerController MainPlayer(int side) =>
            Team(side).Where(c => !c.IsSetter).Select(c => c.Pc).FirstOrDefault() ?? Team(side).Select(c => c.Pc).FirstOrDefault();

        // ------------------------------------------------------------ effect dispatch

        // Runs one effect now; returns a jump bonus for jump_bonus / block_jump.
        public static float Run(FxDef fx, FxCtx ctx)
        {
            var st = Sides[ctx.Side];
            switch (fx.Kind)
            {
                case "power": case "power_streak": case "power_perks": case "cash_bank": return 0f;   // paid in PrepareSpike
                case "next_power": st.NextPower = Mathf.Max(st.NextPower, fx.A); st.NextPowerUntil = Time.time + fx.B; return 0f;
                case "bank": st.Bank = Mathf.Min(st.Bank + fx.A, fx.B); Plugin.Trace($"bank {st.Bank:F2}"); return 0f;
                case "buff": Buff(ctx.Side, fx.S1, fx.A, fx.B); return 0f;
                case "stun": Stun(ctx, fx.S1, fx.A); return 0f;
                case "slow_enemies": foreach (var c in Team(Other(ctx.Side))) c.AddSlow(Mathf.Max(0.2f, fx.A), fx.B); return 0f;
                case "zone": Zone(ctx, fx.A, Mathf.Max(0.2f, fx.B), fx.C, Vfx.Hex(fx.S1)); return 0f;
                case "game_speed": st.GameSpeed = Mathf.Max(st.GameSpeed, fx.A); return 0f;
                case "jump_bonus": return fx.A;
                case "block_jump": return fx.A;
            }
            if (fxKinds.TryGetValue(fx.Kind, out var k) && k.Kind == "ball") BallFx.Apply(fx, ctx);
            else Vfx.Run(fx, ctx);
            return 0f;
        }

        private static void Buff(int side, string stat, float mult, float dur)
        {
            if (dur == -1f)
            {
                Sides[side].MatchBuffs.Add((stat, mult));
                foreach (var c in Team(side)) c.AddBuff(stat, mult, -1f);
                return;
            }
            foreach (var c in Team(side))
                if (stat == "set" ? c.IsSetter : !c.IsSetter) c.AddBuff(stat, mult, dur);
        }

        private static void Stun(FxCtx ctx, string target, float dur)
        {
            var enemies = Team(Other(ctx.Side)).ToList();
            if (enemies.Count == 0) return;
            IEnumerable<ControllerFx> hit;
            switch (target)
            {
                case "all": hit = enemies; break;
                case "blocker": hit = enemies.OrderBy(c => Mathf.Abs(c.transform.position.x)).Take(1); break;
                case "spiker": hit = enemies.Where(c => !c.IsSetter).Take(1); break;
                default:   // hitter: whoever last spiked on that side
                    var last = Sides[Other(ctx.Side)].LastSpiker;
                    hit = enemies.Where(c => last != null ? c.Pc == last : !c.IsSetter).Take(1);
                    break;
            }
            foreach (var c in hit) { c.Stun(dur); Vfx.Bolt(Vfx.Hex("FFE14A"), c.transform.position + Vector3.up * 2f); }
        }

        // ------------------------------------------------------------ ground zones

        internal class ZoneState { public float X, Half, Mult, Until; public int Owner; public GameObject Mark; }
        public static readonly List<ZoneState> Zones = new List<ZoneState>();

        private static void Zone(FxCtx ctx, float width, float mult, float dur, Color color)
        {
            float x = ctx.Ball != null ? ctx.Ball.transform.position.x : 0f;
            float enemySign = ctx.Side == 0 ? 1f : -1f;
            if (x * enemySign < 2f) x = 2f * enemySign;
            var z = new ZoneState { X = x, Half = width / 2f, Mult = mult, Until = Time.time + dur, Owner = ctx.Side };
            z.Mark = Vfx.GroundMark(color, x, width, dur);
            Zones.Add(z);
            Plugin.Trace($"zone at x={x:F1} width {width} x{mult} for {dur}s");
        }

        public static float ZoneMult(ControllerFx c)
        {
            float m = 1f;
            Zones.RemoveAll(z => Time.time > z.Until);
            foreach (var z in Zones)
                if (z.Owner != c.Side && Mathf.Abs(c.transform.position.x - z.X) < z.Half) m = Mathf.Min(m, z.Mult);
            return m;
        }

        public static float GameSpeed => Mathf.Max(Sides[0].GameSpeed, Sides[1].GameSpeed);

        // Development: run every effect of a perk right now as if it fired for that side (all stages at once).
        public static void DebugFire(DataPerk perk, int side)
        {
            var ball = GameManager.Instance.ball != null ? GameManager.Instance.ball.GetComponent<BallMovement>() : null;
            var ctx = new FxCtx(side, MainPlayer(side), ball, perk);
            foreach (var fx in perk.Def.Fx) Run(fx, ctx);
        }
    }

    internal readonly struct FxCtx
    {
        public readonly int Side;
        public readonly PlayerController Source;
        public readonly BallMovement Ball;
        public readonly DataPerk Perk;
        public FxCtx(int side, PlayerController source, BallMovement ball, DataPerk perk) { Side = side; Source = source; Ball = ball; Perk = perk; }
        public FxCtx WithBall(BallMovement ball) => new FxCtx(Side, Source, ball, Perk);
    }
}

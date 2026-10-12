using HangtimeOvertime.Engine;
using HangtimeOvertime.Generated;
using UnityEngine;

namespace HangtimeOvertime.Online
{
    // Perk visuals in an online round. Perks only run on the host; what they draw is sent as events and drawn again on
    // the client: the visual effect kinds (bursts, flashes, auras, afterimages, bolts, rings, trails, tints, slow-mo),
    // ball invisibility and decoys, stuns, ground zones and element call-outs. Their physics is already in the ball's
    // movement, and their shake, zoom and sounds travel with the camera and sound events.
    internal static class FxSync
    {
        public const byte EvVfx = 20, EvCutIn = 21, EvZone = 22, EvStun = 23, EvDecoy = 24, EvInvisible = 25;

        private static int Index(PlayerController pc) =>
            pc != null && HostSync.Current != null ? MatchSync.IndexOf(pc, HostSync.Current.Players) : 255;

        // ------------------------------------------------------------ host: recording (called from the engine)

        public static void Vfx(FxDef fx, FxCtx ctx)
        {
            if (!MatchSync.Recording || fx.Kind == "shake" || fx.Kind == "zoom" || fx.Kind == "sound") return;
            MatchSync.Event(EvVfx).S(fx.Kind).S(fx.S1 ?? "").S(fx.S2 ?? "").F(fx.A).F(fx.B).F(fx.C).F(fx.D).U8(ctx.Side).U8(Index(ctx.Source));
        }

        public static void CutIn(string text) { if (MatchSync.Recording) MatchSync.Event(EvCutIn).S(text ?? ""); }

        public static void Zone(string colorHex, float x, float width, float dur)
        {
            if (MatchSync.Recording) MatchSync.Event(EvZone).S(colorHex).F(x).F(width).F(dur);
        }

        public static void Stun(PlayerController pc, float dur) { if (MatchSync.Recording) MatchSync.Event(EvStun).U8(Index(pc)).F(dur); }

        public static void Decoy(Vector2 pos, Vector2 vel, float gravity, float life)
        {
            if (MatchSync.Recording) MatchSync.Event(EvDecoy).V(pos).V(vel).F(gravity).F(life);
        }

        public static void Invisible(float alpha, float dur) { if (MatchSync.Recording) MatchSync.Event(EvInvisible).F(alpha).F(dur); }

        // ------------------------------------------------------------ client: copying an event's fields, then drawing it

        public static bool Copy(byte type, NetReader r, NetWriter w)
        {
            switch (type)
            {
                case EvVfx: w.S(r.S()).S(r.S()).S(r.S()).F(r.F()).F(r.F()).F(r.F()).F(r.F()).U8(r.U8()).U8(r.U8()); return true;
                case EvCutIn: w.S(r.S()); return true;
                case EvZone: w.S(r.S()).F(r.F()).F(r.F()).F(r.F()); return true;
                case EvStun: w.U8(r.U8()).F(r.F()); return true;
                case EvDecoy: w.V(r.V()).V(r.V()).F(r.F()).F(r.F()); return true;
                case EvInvisible: w.F(r.F()).F(r.F()); return true;
            }
            return false;
        }

        public static bool Play(byte type, NetReader r, PlayerController[] players, BallMovement ball)
        {
            PlayerController At(int i) => i < players.Length ? players[i] : null;
            switch (type)
            {
                case EvVfx:
                {
                    string kind = r.S(), s1 = r.S(), s2 = r.S();
                    float a = r.F(), b = r.F(), c = r.F(), d = r.F();
                    int side = r.U8(); var src = At(r.U8());
                    Engine.Vfx.Run(new FxDef("", "now", kind, a, b, c, d, s1, s2), new FxCtx(side, src, ball, null));
                    return true;
                }
                case EvCutIn: Engine.Vfx.CutIn(r.S()); return true;
                case EvZone: Engine.Vfx.GroundMark(Engine.Vfx.Hex(r.S()), r.F(), r.F(), r.F()); return true;
                case EvStun:
                {
                    var pc = At(r.U8()); float dur = r.F();
                    if (pc == null) return true;
                    pc.GetComponent<ControllerFx>()?.Stun(dur);
                    Engine.Vfx.Bolt(Engine.Vfx.Hex("FFE14A"), pc.transform.position + Vector3.up * 2f);
                    return true;
                }
                case EvDecoy: if (ball != null) BallFx.DecoyCopy(ball, r.V(), r.V(), r.F(), r.F()); return true;
                case EvInvisible: if (ball != null) BallFx.Invisible(ball, r.F(), r.F()); return true;
            }
            return false;
        }
    }
}

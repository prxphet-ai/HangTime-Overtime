using HangtimeOvertime.Engine;
using HarmonyLib;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // Turns the game's touches into perk triggers for whichever team made them. The game calls
    // DoSet/DoSpike/DoTip whenever a hitbox meets the ball, including touches BallMovement then
    // rejects, so only calls that changed the ball's velocity count.
    internal static class TouchPatches
    {
        private static readonly AccessTools.FieldRef<PlayerController, BallMovement> ballOf =
            AccessTools.FieldRefAccess<PlayerController, BallMovement>("ball");
        private static readonly AccessTools.FieldRef<BallMovement, float> blockTimer =
            AccessTools.FieldRefAccess<BallMovement, float>("blockTimer");

        private static Vector2 Velocity(PlayerController pc)
        {
            var ball = ballOf(pc);
            return ball == null ? Vector2.zero : ball.GetComponent<Rigidbody2D>().linearVelocity;
        }

        private static bool Touched(PlayerController pc, Vector2 before) => (Velocity(pc) - before).sqrMagnitude > 0.0001f;

        // Shared by every accepted touch: the other team's queued enemy_touch effects, then a new flight.
        private static void Accepted(PlayerController pc, BallMovement ball)
        {
            int side = Engine.Engine.SideOf(pc);
            BallFx.EnemyTouched(side, ball);
            BallFx.NewFlight(side, ball);
            Engine.Engine.OnAcceptedTouch(side);
        }

        [HarmonyPatch(typeof(PlayerController), "DoSpike")]
        private static class Spike
        {
            [HarmonyPrefix]
            private static void Prefix(PlayerController __instance, out Vector2 __state)
            {
                __state = Velocity(__instance);
                Engine.Engine.PrepareSpike(__instance);
            }

            [HarmonyPostfix]
            private static void Postfix(PlayerController __instance, Vector2 __state)
            {
                bool ok = Touched(__instance, __state);
                var ball = ballOf(__instance);
                if (ok) Accepted(__instance, ball);
                Engine.Engine.CommitSpike(__instance, ball, ok);
            }
        }

        [HarmonyPatch(typeof(PlayerController), "DoTip")]
        private static class Tip
        {
            [HarmonyPrefix]
            private static void Prefix(PlayerController __instance, out Vector2 __state) => __state = Velocity(__instance);

            [HarmonyPostfix]
            private static void Postfix(PlayerController __instance, Vector2 __state)
            {
                if (!Touched(__instance, __state)) return;
                var ball = ballOf(__instance);
                Accepted(__instance, ball);
                Engine.Engine.FireAll("tip", Engine.Engine.SideOf(__instance), __instance, ball);
            }
        }

        [HarmonyPatch(typeof(PlayerController), "DoSet")]
        private static class Set
        {
            [HarmonyPrefix]
            private static void Prefix(PlayerController __instance, out Vector2 __state) => __state = Velocity(__instance);

            [HarmonyPostfix]
            private static void Postfix(PlayerController __instance, Vector2 __state)
            {
                if (!Touched(__instance, __state)) return;
                var ball = ballOf(__instance);
                int side = Engine.Engine.SideOf(__instance);
                Accepted(__instance, ball);
                Engine.Engine.FireAll("set", side, __instance, ball);
                if (__instance.setter) Engine.Engine.FireAll("setter_set", side, __instance, ball);
                if (__state.magnitude > 60f) Engine.Engine.FireAll("dig", side, __instance, ball);
            }
        }

        // The game's big-block branch sets blockTimer to 0.3.
        [HarmonyPatch(typeof(BallMovement), "Block")]
        private static class Block
        {
            [HarmonyPostfix]
            private static void Postfix(BallMovement __instance, Collision2D collision)
            {
                var pc = collision.gameObject.GetComponent<PlayerController>();
                if (pc == null || blockTimer(__instance) < 0.29f) return;
                Accepted(pc, __instance);
                Engine.Engine.FireAll("block", Engine.Engine.SideOf(pc), pc, __instance);
            }
        }

        // A serve starts a new flight before the game calls the server's OnServe techniques.
        [HarmonyPatch(typeof(BallMovement), "Serve")]
        private static class SpinServe
        {
            [HarmonyPostfix]
            private static void Postfix(BallMovement __instance, float direction) => Served(__instance, direction);
        }

        [HarmonyPatch(typeof(BallMovement), "FloatServe")]
        private static class FloatServe
        {
            [HarmonyPostfix]
            private static void Postfix(BallMovement __instance, float direction) => Served(__instance, direction);
        }

        private static void Served(BallMovement ball, float direction)
        {
            int side = direction > 0f ? 0 : 1;   // the server attacks toward +x from the left side
            BallFx.NewFlight(side, ball);
            // the game clears 'serving' just before calling Serve: the server is whoever started holding last
            ControllerFx server = null;
            foreach (var c in ControllerFx.All)
                if (c != null && c.Side == side && (server == null || c.ServeStartTime > server.ServeStartTime)) server = c;
            server?.Served();
        }

        [HarmonyPatch(typeof(PlayerController), "StartServe")]
        private static class ServeHold
        {
            [HarmonyPostfix]
            private static void Postfix(PlayerController __instance) => __instance.GetComponent<ControllerFx>()?.ServeStarted();
        }
    }
}

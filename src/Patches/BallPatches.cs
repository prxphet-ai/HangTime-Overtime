using HangtimeOvertime.Engine;
using HarmonyLib;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    internal static class BallPatches
    {
        // After the game's own physics step (gravity is set there first): net crossing and flight effects, then the
        // out-of-arena watchdog.
        [HarmonyPatch(typeof(BallMovement), "FixedUpdate")]
        private static class Watch
        {
            [HarmonyPostfix]
            private static void Postfix(BallMovement __instance)
            {
                BallFx.Step(__instance);
                Arena.Check(__instance);
            }
        }

        // The arena: floor x -100..100 (top y -10), side walls |x| 70..80 up to y 88. A boosted ball (sky serve, lifts,
        // speed-ups) can clear a side wall; past x 100 there is no floor, so it falls forever: the rally never ends and
        // the camera (which follows the ball's height) stays zoomed in. A ball that leaves the arena ends the rally as an
        // out ball, through the game's own point routine (out: the point goes against the side that touched it last).
        internal static class Arena
        {
            private const float FloorTop = -10f, FloorEnd = 100f, Ceiling = 400f;
            private static float outSince = -1f;
            public static bool Off;                                          // development (Ctrl+0): both arena fixes off, to see the bugs
            private static readonly AccessTools.FieldRef<BallMovement, float> courtOf = AccessTools.FieldRefAccess<BallMovement, float>("COURT_SIZE");

            public static void Check(BallMovement ball)
            {
                if (Off || BallMovement.rallyDone || TitleController.onTitle || TutorialManager.onTutorial || GameManager.gameOver || Online.MatchSync.Mirroring) { outSince = -1f; return; }
                var p = ball.transform.position;
                bool bad = float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.x) || float.IsInfinity(p.y);
                bool gone = bad || Mathf.Abs(p.x) > FloorEnd + 2f || p.y < FloorTop - 4f || p.y > Ceiling;
                if (!gone) { outSince = -1f; return; }
                if (outSince < 0f) { outSince = Time.time; return; }
                if (Time.time - outSince < 0.25f) return;                    // a frame or two past an edge is fine
                outSince = -1f;
                float side = bad ? (BallMovement.lastTouched == 1 ? 1f : -1f) : Mathf.Sign(p.x == 0f ? 1f : p.x);
                // just outside the court on the side it left by, resting on the floor: the game scores it as out
                float court = courtOf(ball) > 1f ? courtOf(ball) : 35.5f;
                var rb = ball.GetComponent<Rigidbody2D>();
                ball.transform.position = new Vector3(side * (court + 20f), FloorTop + 1.5f, ball.transform.position.z);
                rb.linearVelocity = Vector2.zero;
                Plugin.Log.LogInfo($"Ball left the arena at ({(bad ? "NaN" : p.x.ToString("F0"))}, {(bad ? "NaN" : p.y.ToString("F0"))}): scored as out (last touched by side {BallMovement.lastTouched})");
                ball.StartCoroutine(ball.GetPoint(-side));
            }
        }
    }
}

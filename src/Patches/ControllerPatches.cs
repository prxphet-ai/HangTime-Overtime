using HangtimeOvertime.Engine;
using HarmonyLib;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // Every player gets a ControllerFx; stuns and landing lag gate the player's inputs (human or AI).
    internal static class ControllerPatches
    {
        [HarmonyPatch(typeof(PlayerController), "Start")]
        private static class Attach
        {
            [HarmonyPostfix]
            private static void Postfix(PlayerController __instance)
            {
                if (__instance.GetComponent<ControllerFx>() == null) __instance.gameObject.AddComponent<ControllerFx>();
            }
        }

        [HarmonyPatch(typeof(PlayerController), "SetXInput")]
        private static class Move
        {
            [HarmonyPrefix]
            private static void Prefix(PlayerController __instance, ref float input)
            {
                var fx = __instance.GetComponent<ControllerFx>();
                if (fx != null && fx.Stunned) input = 0f;
            }
        }

        [HarmonyPatch(typeof(PlayerController), "UpInputPressed")]
        private static class Up
        {
            [HarmonyPrefix]
            private static bool Prefix(PlayerController __instance)
            {
                Engine.Engine.CurrentInput = __instance;
                var fx = __instance.GetComponent<ControllerFx>();
                if (fx == null) return true;
                if (fx.Stunned) return false;
                return !(fx.Lagging && __instance.getGrounded() && !__instance.IsServing());
            }
        }

        // The game sizes the block box from the block value and was never meant for values under 73 (its lowest level).
        // Below it the box sinks under the player's feet: it lands on the floor first, holds the player just above it,
        // and since blocking only ends when the feet land, the player is stuck in the block pose. Keep the box's bottom
        // where it is at 73 (a lower block still means a lower block jump and a smaller box above).
        [HarmonyPatch(typeof(PlayerController), "MakeBlock")]
        private static class BlockBox
        {
            private static readonly AccessTools.FieldRef<PlayerController, UnityEngine.BoxCollider2D> boxOf =
                AccessTools.FieldRefAccess<PlayerController, UnityEngine.BoxCollider2D>("blockHitBox");
            private static readonly AccessTools.FieldRef<PlayerController, bool> dontScaleOf = AccessTools.FieldRefAccess<PlayerController, bool>("dontScaleBlock");
            private static readonly System.Collections.Generic.Dictionary<UnityEngine.BoxCollider2D, float> lowest =
                new System.Collections.Generic.Dictionary<UnityEngine.BoxCollider2D, float>();

            [HarmonyPrefix]
            private static void Prefix(PlayerController __instance)
            {
                // the bottom the game gives the box at block 73 (offset 0.3; height 4, or the prefab's own if it doesn't scale)
                var box = boxOf(__instance);
                if (box != null && !lowest.ContainsKey(box)) lowest[box] = 0.3f - (dontScaleOf(__instance) ? box.size.y : 4f) / 2f;
            }

            [HarmonyPostfix]
            private static void Postfix(PlayerController __instance)
            {
                var box = boxOf(__instance);
                if (box == null || !lowest.TryGetValue(box, out var floor)) return;
                if (box.size.y < 1f) box.size = new UnityEngine.Vector2(box.size.x, 1f);
                float bottom = box.offset.y - box.size.y / 2f;
                if (bottom < floor) box.offset = new UnityEngine.Vector2(box.offset.x, box.offset.y + (floor - bottom));
            }
        }

        // The invisible wall over the net only reaches y 19.3; a boosted jump went over it onto the other side. Above the
        // wall, a player is held at the line the wall would have stopped them at (their own side of the net).
        [HarmonyPatch(typeof(PlayerController), "FixedUpdate")]
        private static class OwnSide
        {
            private static readonly AccessTools.FieldRef<PlayerController, Rigidbody2D> rbOf = AccessTools.FieldRefAccess<PlayerController, Rigidbody2D>("rb");
            private const float WallHalf = 0.5f;

            [HarmonyPostfix]
            private static void Postfix(PlayerController __instance)
            {
                if (TitleController.onTitle || Online.MatchSync.Mirroring || BallPatches.Arena.Off) return;
                var rb = rbOf(__instance);
                if (rb == null) return;
                float side = __instance.attackDirection;                    // +1 left team, -1 right team
                var col = __instance.GetComponent<Collider2D>();
                float half = col != null ? col.bounds.extents.x : 0.5f;
                float limit = WallHalf + half;
                float x = __instance.transform.position.x;
                if (side > 0f ? x <= -limit : x >= limit) return;
                __instance.transform.position = new Vector3(-side * limit, __instance.transform.position.y, __instance.transform.position.z);
                rb.position = new Vector2(-side * limit, rb.position.y);
                if (rb.linearVelocityX * side > 0f) rb.linearVelocity = new Vector2(0f, rb.linearVelocityY);
            }
        }

        [HarmonyPatch(typeof(PlayerController), "DownInputPressed")]
        private static class Down
        {
            [HarmonyPrefix]
            private static bool Prefix(PlayerController __instance)
            {
                var fx = __instance.GetComponent<ControllerFx>();
                return fx == null || !fx.Stunned;
            }
        }
    }
}

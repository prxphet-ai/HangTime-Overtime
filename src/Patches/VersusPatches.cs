using System.Collections;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HangtimeOvertime.Patches
{
    // Versus 1v1: each Game scene load is one round. Player 1 is the game's own player (left) with the game's bot setter;
    // player 2 takes over the right team's spiker (its AI switched off) and keeps the team's AI setter.
    internal static class VersusPatches
    {
        public static void Init() => SceneManager.sceneLoaded += (scene, _) =>
        {
            if (scene.name != "Game" || TitleController.onTitle || !VersusState.Active) return;
            GameManager.gameNumber = 1;                                   // a regular gym, no difficulty hitbox changes
            TeamRoster.ResumeKey = VersusState.Setup.Team[1];             // player 2's team spawns on the right
        };

        // Called from the match-start hook (instead of opponent scaling) once the right team has spawned.
        public static void SetupMatch(GameManager gm, GameObject team)
        {
            var setup = VersusState.Setup;
            gm.matchLength = Generated.VersusRules.PointsPerRound;

            // equal footing: the right team uses the player's own stat tables, from level 0, without built-in abilities;
            // builds come only from the picks between rounds
            var stats = team.GetComponent<PlayerStats>();
            foreach (var u in stats.statUpgrades)
            {
                var mine = gm.playerStats.GetStat(u.statName);
                if (mine != null) u.levels = (float[])mine.levels.Clone();
                u.currentLevel = 0;
            }
            stats.techniques.Clear();
            stats.techniques.AddRange(VersusState.RightPerks);
            foreach (var ab in team.GetComponentsInChildren<AbilityBooleans>(true))
            {
                ab.canSpikeFreeBalls = ab.blockBoost = ab.perfectBump = ab.absoluteBlock = false;
                ab.skyServe = ab.riskySet = ab.hybridServe = false;
            }

            var all = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var leftHitter = all.FirstOrDefault(Look.IsPlayerOne);
            var leftPartner = Object.FindFirstObjectByType<LocalInputManager>()?.player2;                        // the game's co-op player 2
            var leftSetter = all.FirstOrDefault(p => p.setter && p.transform.position.x < 0f);                  // the game's bot setter
            var rightHitter = team.GetComponentsInChildren<PlayerController>(true).FirstOrDefault(p => !p.setter);
            var rightSetter = team.GetComponentsInChildren<PlayerController>(true).FirstOrDefault(p => p.setter);
            var ball = Object.FindFirstObjectByType<BallMovement>();
            if (leftHitter != null && rightHitter != null) rightHitter.moveSpeed = leftHitter.moveSpeed;
            if (leftSetter != null && rightSetter != null) rightSetter.moveSpeed = leftSetter.moveSpeed;

            // a human on the right replaces that player's AI. The spiker's AI would also have registered it as the right
            // side's server; a human setter becomes a full player like the left side's co-op partner (normal jump, no auto-set).
            if (rightHitter != null)
            {
                var ai = rightHitter.GetComponent<SpikerInput>();
                if (ai != null) ai.enabled = false;
                if (ball != null) ball.opponent = rightHitter;
            }
            if (setup.HumanSetter(1) && rightSetter != null)
            {
                var ai = rightSetter.GetComponent<SetterInput>();
                if (ai != null) ai.enabled = false;
                rightSetter.setter = false;
                var mate = leftPartner != null && setup.Format == "2v2" ? leftPartner : leftHitter;
                if (mate != null) rightSetter.moveSpeed = mate.moveSpeed;
            }
            ServeTurns.Reset(rightHitter, setup.HumanSetter(1) ? rightSetter : null);

            // player 1's side can wear a new team's colours (player 1's own look still goes on top)
            var def = Generated.Teams.All.FirstOrDefault(t => t.Id == setup.Team[0]);
            if (def != null)
                foreach (var pc in all.Where(p => p.transform.position.x < 0f))
                    foreach (var sr in pc.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        if (sr.name == "Core_0") sr.color = Vfx.Hex(def.Jersey);
                        else if (sr.name.StartsWith("Thigh_0")) sr.color = Vfx.Hex(def.Shorts);
                    }

            var driver = new GameObject("Overtime Versus Driver").AddComponent<VersusDriver>();
            foreach (var slot in setup.AllHumans)
            {
                var pc = slot.Side == 0 ? (slot.Role == "partner" ? leftPartner : leftHitter) : (slot.Role == "setter" ? rightSetter : rightHitter);
                driver.Players[slot] = pc;
            }
            Plugin.Log.LogInfo($"Versus {setup.Format} round {VersusState.Round}: " +
                               string.Join(", ", driver.Players.Select(kv => $"P{kv.Key.Number} {kv.Value?.name} ({kv.Key.Input?.Label})")) +
                               $"; {team.name} on the right; AI setters {(setup.HumanSetter(0) || setup.Format == "2v2" ? "-" : leftSetter?.name)}/{(setup.HumanSetter(1) ? "-" : rightSetter?.name)}" +
                               $"; perks {VersusState.PerksOf(0).Count}/{VersusState.RightPerks.Count}");
        }

        // Two humans on the right take turns serving (the game alternates its own co-op pair on the left the same way).
        internal static class ServeTurns
        {
            private static PlayerController first, second;
            private static bool next;
            public static void Reset(PlayerController a, PlayerController b) { first = a; second = b; next = false; }

            public static PlayerController Next(PlayerController current)
            {
                if (second == null || first == null) return current;
                var pick = next ? second : first;
                next = !next;
                return pick;
            }
        }

        [HarmonyPatch(typeof(BallMovement), "opponentGetPoint")]
        private static class RightServer
        {
            [HarmonyPostfix]
            private static void Postfix(BallMovement __instance, ref PlayerController __result)
            {
                if (!VersusState.Active) return;
                __result = ServeTurns.Next(__result);
                __instance.opponent = __result;
            }
        }

        // The game's co-op input (2v2 sets the game to two players on the left) would drive the left pair from fixed keys.
        [HarmonyPatch(typeof(ManualInputRouter), "Update")]
        private static class NoGameCoopKeys { [HarmonyPrefix] private static bool Prefix() => !VersusState.Active; }

        [HarmonyPatch(typeof(LocalInputManager), "OnMoveP2")]
        private static class NoGameMove2 { [HarmonyPrefix] private static bool Prefix() => !VersusState.Active; }

        [HarmonyPatch(typeof(LocalInputManager), "OnUpP2")]
        private static class NoGameUp2 { [HarmonyPrefix] private static bool Prefix() => !VersusState.Active; }

        [HarmonyPatch(typeof(LocalInputManager), "OnDownP2")]
        private static class NoGameDown2 { [HarmonyPrefix] private static bool Prefix() => !VersusState.Active; }

        // A round ends: the game would load its upgrade / lose screens; Versus counts the round instead.
        [HarmonyPatch(typeof(GameManager), "EndGame")]
        private static class RoundEnd
        {
            [HarmonyPriority(Priority.First)]
            [HarmonyPrefix]
            private static bool Prefix(GameManager __instance, string nextScene, ref IEnumerator __result)
            {
                if (!VersusState.Active) return true;
                __result = __instance.done ? Nothing() : VersusFlow.RoundOver(__instance, nextScene == "Lose" ? 1 : 0);
                return false;
            }

            private static IEnumerator Nothing() { yield break; }
        }

        // The game's camera leans towards player 1's side (it tracks the left player and the last touch). In Versus it stays
        // centred on the net, follows the ball only gently, and is zoomed out enough that both serve lines are in view.
        [HarmonyPatch(typeof(CameraController), "FixedUpdate")]
        private static class CentredCamera
        {
            private static readonly AccessTools.FieldRef<CameraController, Camera> cameraOf = AccessTools.FieldRefAccess<CameraController, Camera>("camera");
            private static readonly AccessTools.FieldRef<CameraController, Transform> ballOf = AccessTools.FieldRefAccess<CameraController, Transform>("ball");
            private static float x;

            [HarmonyPostfix]
            private static void Postfix(CameraController __instance)
            {
                if (!VersusState.Active || GameManager.gameOver || TitleController.onTitle) return;
                var cam = cameraOf(__instance);
                var ball = ballOf(__instance);
                if (cam == null || ball == null) return;
                // the game sets the size to its own target every step, so this never compounds
                cam.orthographicSize = Mathf.Max(cam.orthographicSize, Generated.VersusRules.CameraHalfWidth / Mathf.Max(0.5f, cam.aspect));
                x = Mathf.Lerp(x, Mathf.Clamp(ball.position.x * Generated.VersusRules.CameraFollow, -3f, 3f), Time.deltaTime * 2f);
                var p = __instance.transform.position;
                float jitter = Random.Range(-CameraController.shake, CameraController.shake);   // keep the game's hit shake
                __instance.transform.position = new Vector3(x + jitter, p.y, p.z);
            }
        }

        // In Versus each player drives only their own player (VersusDriver); the game's single-player input would
        // otherwise move player 1 from every keyboard key and gamepad.
        [HarmonyPatch(typeof(LocalInputManager), "OnMoveP1")]
        private static class NoGameMove { [HarmonyPrefix] private static bool Prefix() => !VersusState.Active; }

        [HarmonyPatch(typeof(LocalInputManager), "OnUpP1")]
        private static class NoGameUp { [HarmonyPrefix] private static bool Prefix() => !VersusState.Active; }

        [HarmonyPatch(typeof(LocalInputManager), "OnDownP1")]
        private static class NoGameDown { [HarmonyPrefix] private static bool Prefix() => !VersusState.Active; }
    }
}

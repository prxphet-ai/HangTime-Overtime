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

            var all = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var leftHitter = all.FirstOrDefault(Look.IsPlayerOne);
            var leftSetter = all.FirstOrDefault(p => p.setter && p.transform.position.x < 0f);
            var rightHitter = team.GetComponentsInChildren<PlayerController>(true).FirstOrDefault(p => !p.setter);
            var rightSetter = team.GetComponentsInChildren<PlayerController>(true).FirstOrDefault(p => p.setter);
            if (leftHitter != null && rightHitter != null) rightHitter.moveSpeed = leftHitter.moveSpeed;
            if (leftSetter != null && rightSetter != null) rightSetter.moveSpeed = leftSetter.moveSpeed;

            // player 2 replaces the right spiker's AI (which would also have registered itself as the right side's server)
            if (rightHitter != null)
            {
                var ai = rightHitter.GetComponent<SpikerInput>();
                if (ai != null) ai.enabled = false;
                var ball = Object.FindFirstObjectByType<BallMovement>();
                if (ball != null) ball.opponent = rightHitter;
            }

            // player 1 can wear a new team's colours (their own look still goes on top)
            var def = Generated.Teams.All.FirstOrDefault(t => t.Id == setup.Team[0]);
            if (def != null)
                foreach (var pc in all.Where(p => p.transform.position.x < 0f))
                    foreach (var sr in pc.GetComponentsInChildren<SpriteRenderer>(true))
                    {
                        if (sr.name == "Core_0") sr.color = Vfx.Hex(def.Jersey);
                        else if (sr.name.StartsWith("Thigh_0")) sr.color = Vfx.Hex(def.Shorts);
                    }

            var driver = new GameObject("Overtime Versus Driver").AddComponent<VersusDriver>();
            driver.Players[0] = leftHitter;
            driver.Players[1] = rightHitter;
            Plugin.Log.LogInfo($"Versus round {VersusState.Round}: P1 {leftHitter?.name} ({setup.Human(0)?.Input?.Label}) vs P2 {rightHitter?.name} of {team.name} " +
                               $"({setup.Human(1)?.Input?.Label}); setters {leftSetter?.name}/{rightSetter?.name}; perks {VersusState.PerksOf(0).Count}/{VersusState.RightPerks.Count}");
        }

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

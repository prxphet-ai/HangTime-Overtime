using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Generated;
using HangtimeOvertime.Engine;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HangtimeOvertime.Patches
{
    // Infinite and loop runs: which bracket slot each match uses, opponent scaling per tier,
    // and where a won final match goes.
    internal static class RunPatches
    {
        public static void Init() => SceneManager.sceneLoaded += OnSceneLoaded;

        // Runs after the Game scene's Awake/OnEnable and before any Start, which is where the
        // game reads gameNumber (gym, fans, opponent team, hitboxes).
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Upgrade") RunSaves.SaveUpgradeScreen();
            else if (scene.name == "Lose") RunSaves.ClearCurrent();
            else if (scene.name == "Win" && RunState.Mode == RunMode.Normal) RunSaves.ClearCurrent();   // LOOP 2 saves again at its first match
            if (scene.name != "Game" || TitleController.onTitle) return;
            if (RunState.Mode == RunMode.Infinite)
            {
                GameManager.gameNumber = RunState.InfiniteSlot;
            }
            else if (RunState.Mode == RunMode.Loop && GameManager.gameNumber > 8)
            {
                RunState.EnterLoop(RunState.Loop + 1);
                GameManager.gameNumber = 1;
                GameManager.Instance.tournamentLineUps?.NewLineUp();
            }
        }

        // Infinite and Loop: a random opponent for this slot. The game picks its team from fields and
        // the line-up by gameNumber, so those are pointed at the chosen team for this one call.
        [HarmonyPatch(typeof(GameManager), "OnStart")]
        private static class TeamSelect
        {
            [HarmonyPrefix]
            private static void Prefix(GameManager __instance, out (GameObject m1, GameObject m2, GameObject fin, List<GameObject> combo) __state)
            {
                __state = (__instance.midBoss1, __instance.midBoss2, __instance.finalBoss, __instance.comboTeams);
                TeamRoster.ClearChoice();
                int slot = GameManager.gameNumber;
                if (TitleController.onTitle || slot < 1 || slot > 8) return;
                if (RunState.Mode == RunMode.Normal && TeamRoster.ResumeKey == null) return;   // Classic: the game's own bracket
                var prefab = TeamRoster.Choose(slot);
                switch (slot)
                {
                    case 3: __instance.midBoss1 = prefab; break;
                    case 6: __instance.midBoss2 = prefab; break;
                    case 7: __instance.comboTeams = new List<GameObject>(1) { prefab }; break;
                    case 8: __instance.finalBoss = prefab; break;
                    default: TeamRoster.ForcedPrefab = prefab; break;
                }
            }

            [HarmonyPostfix]
            private static void Postfix(GameManager __instance, (GameObject m1, GameObject m2, GameObject fin, List<GameObject> combo) __state)
            {
                __instance.midBoss1 = __state.m1; __instance.midBoss2 = __state.m2;
                __instance.finalBoss = __state.fin; __instance.comboTeams = __state.combo;
                TeamRoster.ForcedPrefab = null;
            }
        }

        [HarmonyPatch(typeof(TournamentLineUps), "GetTeam")]
        private static class LineUpPick
        {
            [HarmonyPrefix]
            private static bool Prefix(ref GameObject __result)
            {
                if (TeamRoster.ForcedPrefab == null) return true;
                __result = TeamRoster.ForcedPrefab;
                return false;
            }
        }

        [HarmonyPatch(typeof(GameManager), "OnStart")]
        private static class MatchStart
        {
            [HarmonyPostfix]
            private static void Postfix(GameManager __instance)
            {
                if (TitleController.onTitle) return;
                Engine.Engine.ResetMatch();
                var team = __instance.currentTeam;
                if (team == null) return;
                int slot = GameManager.gameNumber;
                if (TeamRoster.Current != null) TeamRoster.Dress(team, TeamRoster.Current, slot);
                OpponentScaling.LastGiven.Clear();
                if (RunState.Mode != RunMode.Normal) OpponentScaling.Apply(team, TeamRoster.Current, slot);
                RunSaves.SaveMatchStart(team, TeamRoster.CurrentKey ?? team.name.Replace("(Clone)", "").Trim(), OpponentScaling.LastGiven);
                Engine.Engine.FirePassives();
                Plugin.Log.LogInfo($"Match start: mode={RunState.Mode} slot={slot} round={OpponentScaling.Round} " +
                                   $"infiniteMatch={RunState.InfiniteMatch} loop={RunState.Loop} vs {__instance.opponentTeam?.teamName}");
                if (Plugin.DumpScenes.Value) { Debugging.Dumps.Opponent(team); Debugging.Dumps.PlayerParts(); }
            }
        }

        [HarmonyPatch(typeof(OpponentBanner), "Start")]
        private static class TeamBanner
        {
            private static readonly AccessTools.FieldRef<OpponentBanner, SpriteRenderer> sprite =
                AccessTools.FieldRefAccess<OpponentBanner, SpriteRenderer>("mySprite");

            [HarmonyPostfix]
            private static void Postfix(OpponentBanner __instance) => TeamRoster.LabelBanner(sprite(__instance));
        }

        [HarmonyPatch(typeof(DialogueList), "GetDialogue", typeof(string))]
        private static class Comment
        {
            [HarmonyPostfix]
            private static void Postfix(string teamName, ref string __result)
            {
                if (string.IsNullOrEmpty(__result) && TeamRoster.Current != null && teamName == TeamRoster.Current.Name) __result = TeamRoster.Current.Comment;
            }
        }

        [HarmonyPatch(typeof(GameManager), "EndGame")]
        private static class EndMatch
        {
            [HarmonyPrefix]
            private static void Prefix(GameManager __instance, ref string nextScene)
            {
                if (__instance.done || RunState.Mode == RunMode.Normal) return;
                if (nextScene == "Win") nextScene = "Upgrade";     // the bracket repeats instead of ending
                if (RunState.Mode != RunMode.Infinite) return;
                if (nextScene == "Upgrade")
                {
                    RunState.InfiniteWins++;
                    RunState.InfiniteMatch++;
                }
                else if (nextScene == "Lose")
                {
                    RunState.EndInfinite();
                }
            }
        }

        [HarmonyPatch(typeof(TitleCard), "MatchTitle")]
        private static class Banner
        {
            [HarmonyPrefix]
            private static bool Prefix(TitleCard __instance)
            {
                if (RunState.Mode == RunMode.Normal || TutorialManager.onTutorial) return true;
                int g = Mathf.Clamp(GameManager.gameNumber, 0, __instance.MatchTitles.Count - 1);
                string text = RunState.ModeDef.Banner
                    .Replace("{n}", RunState.InfiniteMatch.ToString())
                    .Replace("{loop}", RunState.Loop.ToString())
                    .Replace("{bracket}", __instance.MatchTitles[g]);
                __instance.TextSlide(text);
                return false;
            }
        }
    }
}

using System.Collections;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Generated;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HangtimeOvertime.Patches
{
    internal class InfiniteButtonMarker : MonoBehaviour { }

    internal class LoopContinueMarker : MonoBehaviour { }

    // Title INFINITE button, Win-screen "Keep going", results on the lose screen, and modes
    // switching on the game's own buttons.
    internal static class MenuPatches
    {
        public static void Init() => SceneManager.sceneLoaded += (scene, _) => { if (scene.name == "Game") InjectInfiniteButton(); };

        private static string Fill(string s) => s
            .Replace("{bestInfiniteWins}", RunState.Save.bestInfiniteWins.ToString())
            .Replace("{bestLoop}", RunState.Save.bestLoop.ToString())
            .Replace("{wins}", RunState.InfiniteWins.ToString())
            .Replace("{best}", RunState.Save.bestInfiniteWins.ToString())
            .Replace("{loop}", RunState.Loop.ToString());

        private static void SetLabels(GameObject button, string text, string subtext)
        {
            var labels = button.GetComponentsInChildren<TextMeshPro>(true).OrderBy(l => l.name).ToArray();
            if (labels.Length > 0) labels[0].text = text;
            for (int i = 1; i < labels.Length; i++) labels[i].text = subtext == "-" ? "" : subtext;
        }

        private static void InjectInfiniteButton()
        {
            var classic = Object.FindObjectsByType<TitleButton>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(b => Traverse.Create(b).Field("myNumberOfPlayers").GetValue<int>() == 1);
            var group = classic != null ? classic.GetComponentInParent<ButtonGroup>(true) : null;
            if (group == null) { Plugin.Log.LogWarning("Title menu not found; no Infinite button"); return; }
            if (group.buttons.Any(b => b != null && b.GetComponent<InfiniteButtonMarker>())) return;

            var clone = Object.Instantiate(classic.gameObject, classic.transform.parent);
            clone.name = "Infinite Button";
            clone.AddComponent<InfiniteButtonMarker>();
            clone.transform.position = classic.transform.position + new Vector3(0f, Ui.TitleInfiniteButton.OffsetY, 0f);
            SetLabels(clone, Ui.TitleInfiniteButton.Text, Fill(Ui.TitleInfiniteButton.Subtext));

            int at = group.buttons.FindIndex(b => b != null && b.GetComponent<QuitButton>());
            if (at < 0) at = group.buttons.Count;
            else group.buttons[at].transform.position += new Vector3(0f, Ui.TitleQuitShift.OffsetY, 0f);
            var controller = clone.GetComponent<ButtonController>();
            controller.group = group;
            group.buttons.Insert(at, controller);
        }

        [HarmonyPatch(typeof(TitleButton), "Click")]
        private static class TitleMenu
        {
            [HarmonyPrefix]
            private static void Prefix(TitleButton __instance)
            {
                if (__instance.GetComponent<InfiniteButtonMarker>()) RunState.StartInfinite();
                else { RunState.Mode = RunMode.Normal; RunState.Loop = 1; Engine.StatCards.ResetRun(); }
            }
        }

        [HarmonyPatch(typeof(RestartButton), "Click")]
        private static class LoopContinue
        {
            [HarmonyPrefix]
            private static bool Prefix(RestartButton __instance)
            {
                if (!__instance.GetComponent<LoopContinueMarker>()) return true;
                RunState.EnterLoop(RunState.Loop + 1);
                GameManager.gameNumber = 1;
                GameManager.Instance.tournamentLineUps?.NewLineUp();
                __instance.StartCoroutine(LoadNextLoop());
                return false;
            }

            private static IEnumerator LoadNextLoop()
            {
                GameManager.Instance.transition.Play();
                yield return new WaitForSecondsRealtime(0.5f);
                GameManager.Instance.ResetGame();
                PauseController.paused = false;
                TitleController.onTitle = false;
                SceneManager.LoadScene("Game");
            }
        }

        [HarmonyPatch(typeof(RestartButton), "Click")]
        private static class Restart
        {
            [HarmonyPostfix]
            private static void Postfix(RestartButton __instance)
            {
                if (__instance.GetComponent<LoopContinueMarker>()) return;
                if (RunState.Mode == RunMode.Infinite && !__instance.toTitle && !__instance.toTutorial) RunState.StartInfinite();
                else { RunState.Mode = RunMode.Normal; RunState.Loop = 1; Engine.StatCards.ResetRun(); }
            }
        }

        [HarmonyPatch(typeof(WinManager), "Start")]
        private static class WinScreen
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                var restart = Object.FindObjectsByType<RestartButton>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .OrderByDescending(b => b.gameObject.activeInHierarchy).FirstOrDefault();
                if (restart == null) { Plugin.Log.LogWarning("Win screen: no restart button to copy"); return; }
                var clone = Object.Instantiate(restart.gameObject, restart.transform.parent);
                clone.name = "Keep Going Button";
                clone.AddComponent<LoopContinueMarker>();
                clone.transform.position = restart.transform.position + new Vector3(0f, Ui.WinContinueButton.OffsetY, 0f);
                SetLabels(clone, Ui.WinContinueButton.Text.Replace("2", (RunState.Loop + 1).ToString()), Ui.WinContinueButton.Subtext);
                var group = restart.GetComponentInParent<ButtonGroup>(true);
                var controller = clone.GetComponent<ButtonController>();
                if (group != null && controller != null)
                {
                    controller.group = group;
                    group.buttons.Insert(Mathf.Max(0, group.buttons.IndexOf(restart.GetComponent<ButtonController>())), controller);
                }
                Plugin.Log.LogInfo($"Win screen: added '{clone.name}' under {restart.transform.parent?.name}");
            }
        }

        [HarmonyPatch(typeof(LoseController), "HandleLoss")]
        private static class LoseScreen
        {
            [HarmonyPostfix]
            private static void Postfix(LoseController __instance)
            {
                if (RunState.Mode == RunMode.Normal) return;
                var t = Traverse.Create(__instance);
                if (t.Field("loseSprite").GetValue<GameObject>() != null) return;
                var stats = t.Field("stats").GetValue<TextMeshPro>();
                if (stats == null) return;
                string line = RunState.Mode == RunMode.Infinite ? Ui.LoseResultLine.Text : Ui.LoseResultLine.Subtext;
                stats.text += "\n" + Fill(line);
            }
        }

        [HarmonyPatch(typeof(AchievementManager), "Unlock")]
        private static class Achievements
        {
            [HarmonyPrefix]
            private static bool Prefix() => !RunState.ModeDef.BlockAchievements;
        }
    }
}

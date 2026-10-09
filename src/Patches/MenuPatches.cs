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
        public static void Init() => SceneManager.sceneLoaded += (scene, _) => { if (scene.name == "Game") { InjectInfiniteButton(); AddTitleLogo(); if (!TitleController.onTitle) ContinueMenu.DropStandIn(); } };

        // "OVERTIME" under the game's own HangTime! logo, then the credit line (art: tools/title_logo.py, shipped in the
        // plugin's title folder). Children of the logo sprite, so they follow its intro animation.
        private static void AddTitleLogo()
        {
            var title = Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(r => r.name == "Title" && r.transform.parent != null && r.transform.parent.name == "Title Holder");
            if (title == null) { Plugin.Log.LogWarning("Title logo not found; no Overtime logo"); return; }
            if (title.transform.Find("Overtime Logo") != null) return;
            var logo = title.bounds;
            var over = TitleSprite(title, "Overtime Logo", Ui.TitleOvertime.Text, logo.size.x * 0.48f, 1);
            if (over == null) return;
            float cx = logo.center.x + logo.size.x * 0.04f;
            float overTop = logo.min.y + Ui.TitleOvertime.OffsetY;
            over.transform.position = new Vector3(cx, overTop - over.bounds.size.y / 2f, title.transform.position.z);
            var credit = TitleSprite(title, "Overtime Credit", Ui.TitleCredit.Text, logo.size.x * 0.34f, 2);
            if (credit != null)
                credit.transform.position = new Vector3(cx, over.bounds.min.y + Ui.TitleCredit.OffsetY - credit.bounds.size.y / 2f, title.transform.position.z);
            // the title menu's group (other menus use the same object name), found the same way as for the INFINITE button
            var classic = Object.FindObjectsByType<TitleButton>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(b => Traverse.Create(b).Field("myNumberOfPlayers").GetValue<int>() == 1);
            var group = classic != null ? classic.GetComponentInParent<ButtonGroup>(true) : null;
            if (group != null)
                foreach (var b in group.buttons.Where(b => b != null))
                    b.transform.position += new Vector3(0f, Ui.TitleButtonsShift.OffsetY, 0f);
            Plugin.Trace($"title logo {logo.min.y:F2}..{logo.max.y:F2} x {logo.min.x:F2}..{logo.max.x:F2}; overtime {over.bounds.min.y:F2}..{over.bounds.max.y:F2}" +
                         (group != null ? "; buttons " + string.Join(", ", group.buttons.Where(b => b != null).Select(b => $"{b.name} {b.GetComponent<Collider2D>()?.bounds.max.y:F2}")) : ""));
        }

        private static SpriteRenderer TitleSprite(SpriteRenderer title, string name, string file, float worldWidth, int order)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "title", file);
            if (!System.IO.File.Exists(path)) { Plugin.Log.LogWarning("No title art " + path); return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.LoadImage(System.IO.File.ReadAllBytes(path));
            var go = new GameObject(name);
            go.transform.SetParent(title.transform, false);
            go.transform.localRotation = Quaternion.identity;
            var lossy = title.transform.lossyScale.x;
            go.transform.localScale = Vector3.one / (lossy == 0f ? 1f : lossy);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width / worldWidth);
            sr.sortingLayerID = title.sortingLayerID;
            sr.sortingOrder = title.sortingOrder + order;
            sr.color = title.color;
            return sr;
        }

        private static string Fill(string s) => s
            .Replace("{bestInfiniteWins}", RunState.Save.bestInfiniteWins.ToString())
            .Replace("{bestLoop}", RunState.Save.bestLoop.ToString())
            .Replace("{wins}", RunState.InfiniteWins.ToString())
            .Replace("{best}", RunState.Save.bestInfiniteWins.ToString())
            .Replace("{loop}", RunState.Loop.ToString());

        public static void SetButtonLabels(GameObject button, string text, string subtext)
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
            if (!TitleController.onTitle) return;

            var clone = Object.Instantiate(classic.gameObject, classic.transform.parent);
            clone.name = "Infinite Button";
            clone.AddComponent<InfiniteButtonMarker>();
            clone.transform.position = classic.transform.position + new Vector3(0f, Ui.TitleInfiniteButton.OffsetY, 0f);
            SetButtonLabels(clone, Ui.TitleInfiniteButton.Text, Fill(Ui.TitleInfiniteButton.Subtext));

            int at = group.buttons.FindIndex(b => b != null && b.GetComponent<QuitButton>());
            if (at < 0) at = group.buttons.Count;
            else group.buttons[at].transform.position += new Vector3(0f, Ui.TitleQuitShift.OffsetY, 0f);
            var controller = clone.GetComponent<ButtonController>();
            controller.group = group;
            group.buttons.Insert(at, controller);
            ContinueMenu.Label(classic.gameObject, clone);
        }

        [HarmonyPatch(typeof(TitleButton), "Click")]
        private static class TitleMenu
        {
            [HarmonyPrefix]
            private static bool Prefix(TitleButton __instance)
            {
                bool infinite = ContinueMenu.IsInfinite(__instance);
                if (!ContinueMenu.Bypass && ContinueMenu.Players(__instance) == 1 && RunSaves.Has(infinite))
                {
                    ContinueMenu.Show(__instance, infinite);     // CONTINUE / NEW RUN / BACK
                    return false;
                }
                ContinueMenu.Bypass = false;
                if (infinite) RunState.StartInfinite();
                else { RunState.Mode = RunMode.Normal; RunState.Loop = 1; Engine.StatCards.ResetRun(); }
                return true;
            }
        }

        // a perk or stat card was picked: the run is saved before the next match loads
        [HarmonyPatch(typeof(UpgradeManager), "LoadGame")]
        private static class PickSaved
        {
            [HarmonyPrefix]
            private static void Prefix() => RunSaves.SaveAfterPick();
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
                SetButtonLabels(clone, Ui.WinContinueButton.Text.Replace("2", (RunState.Loop + 1).ToString()), Ui.WinContinueButton.Subtext);
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

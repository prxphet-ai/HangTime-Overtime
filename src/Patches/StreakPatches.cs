using System.Collections;
using System.Linq;
using HangtimeOvertime.Core;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HangtimeOvertime.Patches
{
    // Win streaks: every finished match is recorded for its mode (Stats); the streak shows on the next screen (the
    // upgrade screen after a win, the lose screen after a loss, the Versus match-end screen).
    internal static class StreakPatches
    {
        private static string lastLine;                  // what to show after the match that just ended
        public static string VersusLine;                 // the Versus match-end screen's line on this PC (or null)

        public static void Init() => SceneManager.sceneLoaded += (scene, _) =>
        {
            if (scene.name == "Upgrade" && lastLine != null) Runner.Instance.StartCoroutine(ShowBadge(lastLine));
        };

        private static string ModeKey() =>
            RunState.Mode == RunMode.Infinite ? (GameManager.Instance.numberOfPlayers == 2 ? Records.CoopInfinite : Records.Infinite) : RunState.Draft != null ? Records.Draft : GameManager.Instance.numberOfPlayers == 2 ? Records.Coop : Records.Classic;

        // the game's match end (Classic, Co-op, Loop, Infinite); Versus has its own round flow
        [HarmonyPatch(typeof(GameManager), "EndGame")]
        private static class MatchEnd
        {
            [HarmonyPriority(Priority.First)]
            [HarmonyPrefix]
            private static void Prefix(GameManager __instance, string nextScene)
            {
                if (__instance.done || VersusState.Active || TitleController.onTitle || TutorialManager.onTutorial) return;
                bool won = nextScene != "Lose";
                RunClock.MatchEnded(__instance.playerPoints, __instance.opponentPoints, __instance.opponentTeam?.teamName);
                // the Classic speedrun board: a solo Classic clear (not a daily / draft cup, not co-op, not a loop)
                if (nextScene == "Win" && RunState.Mode == RunMode.Normal && RunState.Daily == null && RunState.Draft == null && __instance.numberOfPlayers == 1)
                {
                    RunClock.Stop();
                    Online.Leaderboards.Finish(Online.Leaderboards.Classic, RunClock.Seconds, RunClock.LogText, "lineup:" + TournamentLineUps.currentLineUp);
                }
                string mode = ModeKey();
                var (_, before) = Records.Record(mode, won);
                if (won && RunState.Mode == RunMode.Infinite) Records.InfiniteRound(RunState.InfiniteMatch);
                if (nextScene == "Win" && RunState.Mode == RunMode.Normal) Records.ClassicRunWon();
                lastLine = Records.Line(mode, won, before);
            }
        }

        // after a loss: the game's stats lines stay as they are (no wrapping, so a long team name doesn't push them into the
        // button the lose screen's animation places below them); the streak shows as a banner once the screen has faded in
        [HarmonyPatch(typeof(LoseController), "HandleLoss")]
        private static class LoseLine
        {
            [HarmonyPostfix]
            private static void Postfix(LoseController __instance)
            {
                if (lastLine == null || VersusState.Active) return;
                var t = Traverse.Create(__instance);
                if (t.Field("loseSprite").GetValue<GameObject>() != null) return;          // the variant without the stats text
                var stats = t.Field("stats").GetValue<TextMeshPro>();
                if (stats != null) stats.textWrappingMode = TextWrappingModes.NoWrap;
                Runner.Instance.StartCoroutine(LoseBanner(lastLine));
                lastLine = null;
            }
        }

        private static IEnumerator LoseBanner(string text)
        {
            yield return new WaitForSecondsRealtime(1.5f);
            if (!TitleController.onTitle) StreakBadge.Show(text);
        }

        // a Versus match ended: this PC's side (local: player 1's; online: wherever this PC's own players are)
        // co-op Infinite (lobby): each match once (a friend's PC sees several screens per match)
        private static int coopRecorded = -1;
        public static void RecordCoop(int round, bool won, int wins)
        {
            if (round == coopRecorded) return;
            coopRecorded = round;
            Records.Record(Records.CoopInfinite, won);
            if (!won) { Records.CoopInfiniteRun(wins); coopRecorded = -1; }
        }

        public static void RecordVersus(int winner)
        {
            VersusLine = null;
            var setup = VersusState.Setup;
            if (setup == null) return;
            int side;
            if (!Online.Net.Online) side = 0;
            else
            {
                var mine = setup.AllHumans.Where(h => Online.Net.IsHost ? h.Input?.Kind != InputKind.Remote : Online.Net.MyDevice(h.Input) >= 0).Select(h => h.Side).Distinct().ToList();
                if (mine.Count != 1) return;                 // this PC plays both sides (or none): no streak
                side = mine[0];
            }
            string mode = Online.Net.Online ? Records.VersusOnline : Records.VersusLocal;
            var (_, before) = Records.Record(mode, winner == side);
            Records.Data.versusMatches++;
            Records.Save();
            VersusLine = Records.Line(mode, winner == side, before);
        }

        // the upgrade screen's GAME STATS card gets the line under its own stats (a banner if the card isn't found)
        private static IEnumerator ShowBadge(string text)
        {
            yield return null;
            yield return null;
            var card = Object.FindObjectsByType<TextMeshPro>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(t => t.text != null && t.text.IndexOf("score", System.StringComparison.OrdinalIgnoreCase) >= 0 && t.text.Contains("\n"));
            if (card != null) card.text += "\n" + text;
            else StreakBadge.Show(text);
            lastLine = null;
        }
    }

    // a small banner at the top of the screen (the upgrade screen's streak line)
    internal class StreakBadge : OvertimePanel
    {
        public static void Show(string text)
        {
            var go = new GameObject("Overtime Streak");
            var b = go.AddComponent<StreakBadge>();
            if (!b.InitKit(250)) { Destroy(go); return; }
            b.Box("Badge", new Vector2(0f, b.H / 2f - 1.6f), new Vector2(18f, 2.2f), new Color(0.05f, 0.42f, 0.66f, 0.92f), 0);
            b.Text(text, new Vector2(0f, b.H / 2f - 1.6f), 0.6f, 17f, new Color(1f, 0.85f, 0.35f));
        }
    }
}

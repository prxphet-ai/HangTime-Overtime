using HangtimeOvertime.Engine;
using HarmonyLib;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    internal static class TimePatches
    {
        // CameraController.Update sets timeScale to 1 every frame unless paused or in a hit
        // freeze-frame; perks only change that normal-play value (game_speed, slow motion).
        [HarmonyPatch(typeof(CameraController), "Update")]
        private static class GameTime
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (Time.timeScale != 1f || PauseController.paused || GameManager.gameOver
                    || TitleController.onTitle || TutorialManager.onTutorial) return;
                float t = Engine.Engine.GameSpeed;
                if (Time.unscaledTime < Vfx.SlowmoUntil) t = Mathf.Min(t, Vfx.SlowmoScale);
                if (t == 1f) return;
                Time.timeScale = t;
                Time.fixedDeltaTime = t * 0.02f;
            }
        }
    }
}

using System.Collections;
using HangtimeOvertime.Engine;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HangtimeOvertime.Patches
{
    // The player's custom look wherever player one is drawn: matches, the title screen, and the
    // upgrade / win / lose scenes (which draw the player as a separate character in number 13).
    internal static class LookPatches
    {
        public static void Init() => SceneManager.sceneLoaded += (scene, _) =>
        {
            if (scene.name == "Upgrade" || scene.name == "Win" || scene.name == "Lose") Runner.Instance.StartCoroutine(ApplySoon());
        };

        private static IEnumerator ApplySoon()
        {
            Look.ApplyToSceneCharacters();
            yield return new WaitForSeconds(0.5f);      // characters that switch on after the intro
            Look.ApplyToSceneCharacters();
        }

        [HarmonyPatch(typeof(PlayerController), "Start")]
        private static class PlayerSpawned
        {
            [HarmonyPostfix]
            private static void Postfix(PlayerController __instance)
            {
                if (Look.IsPlayerOne(__instance)) Look.Apply(Look.RigOf(__instance), Look.Current);
            }
        }
    }

    // A persistent object to run coroutines from static code.
    internal class Runner : MonoBehaviour
    {
        private static Runner instance;
        public static Runner Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("Overtime Runner");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<Runner>();
                }
                return instance;
            }
        }
    }
}

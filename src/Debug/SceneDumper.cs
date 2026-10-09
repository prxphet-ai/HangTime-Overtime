using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HangtimeOvertime.Debugging
{
    // Development aid: logs each loaded scene's hierarchy (name, position, components)
    // so UI placement can be read from the real game. Off unless DumpScenes is enabled.
    internal static class SceneDumper
    {
        public static void Hook()
        {
            SceneManager.sceneLoaded += (scene, _) => { Dump(scene); if (scene.name == "Game") Dumps.GameData(); };
        }

        private static void Dump(Scene scene)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== SCENE {scene.name} ===");
            foreach (var root in scene.GetRootGameObjects())
                Walk(root.transform, 0, sb);
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static void Walk(Transform t, int depth, StringBuilder sb)
        {
            if (depth > 6) return;
            sb.Append(' ', depth * 2).Append(t.name)
              .Append(t.gameObject.activeSelf ? "" : " [off]")
              .Append(" @").Append(t.position.ToString("F1")).Append(" s").Append(t.localScale.ToString("F2")).Append(" :");
            foreach (var c in t.GetComponents<Component>())
                if (c != null && !(c is Transform)) sb.Append(' ').Append(c.GetType().Name);
            sb.AppendLine();
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, sb);
        }
    }
}

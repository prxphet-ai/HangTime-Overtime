using System;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace HangtimeOvertime.Patches
{
    // On launch: is there a newer Overtime release on GitHub? Asks GitHub's public API for the latest release once, in the
    // background (nothing about the player is sent). A newer version puts a notice under the title bar; clicking it opens
    // the releases page. Offline, slow or odd replies are ignored silently. [General] CheckForUpdates turns it off.
    internal static class UpdateCheck
    {
        private const string Api = "https://api.github.com/repos/prxphet-ai/HangTime-Overtime/releases/latest";
        public const string ReleasesPage = "https://github.com/prxphet-ai/HangTime-Overtime/releases/latest";
        private static bool started;
        public static string Latest;                       // the newer version found, or null

        public static void Start()
        {
            if (started || !Plugin.CheckForUpdates.Value) return;
            started = true;
            Runner.Instance.StartCoroutine(Ask());
        }

        private static IEnumerator Ask()
        {
            string url = Api;
            if (Plugin.DevKeys.Value) { var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, "-ovt-updateurl"); if (i >= 0 && i + 1 < args.Length) url = args[i + 1]; }
            float t0 = Time.realtimeSinceStartup;
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 8;
                req.SetRequestHeader("User-Agent", "HangtimeOvertime");
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Plugin.Log.LogInfo($"Update check: no answer after {Time.realtimeSinceStartup - t0:F1} s ({req.error}); nothing shown"); yield break; }
                string tag;
                try
                {
                    var m = Regex.Match(req.downloadHandler.text, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                    if (!m.Success) yield break;
                    tag = m.Groups[1].Value;
                }
                catch { yield break; }
                string mine = Installed();
                if (Newer(tag, mine))
                {
                    Latest = tag.TrimStart('v', 'V');
                    TitleBar.Notice = $"OVERTIME v{Latest} IS OUT (YOU HAVE v{mine})  ·  CLICK TO OPEN THE RELEASES PAGE";
                    TitleBar.NoticeClick = () => Application.OpenURL(ReleasesPage);
                    Plugin.Log.LogInfo($"Update check: v{Latest} is available (installed v{mine})");
                }
                else Plugin.Log.LogInfo($"Update check: up to date (latest release {tag}, installed v{mine})");
            }
        }

        // development (DevKeys + -ovt-pretendversion X): check as if X were installed
        private static string Installed()
        {
            if (!Plugin.DevKeys.Value) return Plugin.Version;
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-ovt-pretendversion");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : Plugin.Version;
        }

        // "v0.3.2" > "0.3.1"; anything unreadable counts as not newer
        public static bool Newer(string candidate, string current)
        {
            var a = Parse(candidate); var b = Parse(current);
            if (a == null || b == null) return false;
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
                if (x != y) return x > y;
            }
            return false;
        }

        private static int[] Parse(string v)
        {
            var m = Regex.Match(v ?? "", @"(\d+(?:\.\d+)*)");
            if (!m.Success) return null;
            try { return Array.ConvertAll(m.Groups[1].Value.Split('.'), int.Parse); } catch { return null; }
        }
    }
}

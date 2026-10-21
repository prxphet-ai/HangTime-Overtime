using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HangtimeOvertime.Core;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // REPORT BUG (title bar and pause menu): shows exactly what would be sent, then opens a pre-filled GitHub issue (the
    // player describes the bug there and submits it with their GitHub account) or copies the same text to the clipboard.
    // The report has the mod and game versions, the current mode and the end of the BepInEx log with personal details
    // taken out (the Windows user name in paths, Steam and player names, IP addresses, Steam ids, e-mail addresses).
    internal class BugReport : OvertimePanel
    {
        private const string Repo = "https://github.com/prxphet-ai/HangTime-Overtime";
        private static BugReport open;
        public static bool IsOpen => open != null;
        private string title, body;
        private TextMeshPro status;

        public static void Open()
        {
            if (open != null) return;
            var go = new GameObject("Overtime Bug Report");
            open = go.AddComponent<BugReport>();
            if (!open.InitKit(400)) { Destroy(go); open = null; return; }
            open.Build();
        }

        private void Build()
        {
            if (TitleController.onTitle) HoldTitle();
            (title, body) = Compose();
            if (Plugin.DevKeys.Value)
            {
                var u = IssueUrl(title, body);
                Plugin.Log.LogInfo($"[report] url {u.Length} chars: {u.Substring(0, Math.Min(160, u.Length))}");
                string me = Environment.UserName;
                Plugin.Log.LogInfo("[report] scrub test: " + Scrub("C:\\Users\\" + me + "\\AppData path; " + me + " joined " + me.ToUpperInvariant() +
                                   "'s lobby; dialling 192.168.1.42:27515; id 76561198012345678; me@example.com; BepInEx 5.4.23.5"));
            }
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.75f), 0);
            float pw = W * 0.86f, ph = H * 0.88f, top = ph / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.98f), 1);
            Text("REPORT A BUG", new Vector2(0f, top - 1.8f), 1.1f, pw - 2f, Color.white);
            Text("THIS IS EVERYTHING THAT WILL BE SENT. NAMES, YOUR WINDOWS USER NAME AND IP ADDRESSES HAVE BEEN TAKEN OUT.\n" +
                 "OPEN ON GITHUB opens a new issue in your browser: describe the bug at the top and submit it (needs a GitHub account).\n" +
                 "COPY puts the same text on your clipboard to paste anywhere (Discord, e-mail...).",
                 new Vector2(0f, top - 4.2f), 0.42f, pw - 3f, new Color(0.85f, 0.95f, 1f), height: 3f, wrap: true);
            Box("Preview", new Vector2(0f, -1.4f), new Vector2(pw - 2.4f, ph - 12.4f), new Color(0.02f, 0.2f, 0.34f, 1f), 2);
            // the preview: the whole report (the log part is cut to what fits; the full text is what gets sent)
            var lines = body.Replace("\r", "").Split('\n');
            string shown = string.Join("\n", lines.Take(26)) + (lines.Length > 26 ? $"\n... ({lines.Length - 26} more lines, all sent)" : "");
            Text("<noparse>" + "TITLE: " + title + "\n\n" + shown + "</noparse>", new Vector2(0f, -1.4f), 0.44f, pw - 4f, Color.white, TextAlignmentOptions.TopLeft, height: ph - 13.2f, wrap: true);
            float by = -ph / 2f + 1.7f;
            Button("OPEN ON GITHUB", new Vector2(-pw * 0.26f, by), new Vector2(pw * 0.24f, 2.3f), OpenIssue, 0.6f);
            Button("COPY", new Vector2(0f, by), new Vector2(pw * 0.18f, 2.3f), Copy, 0.6f);
            Button("CANCEL", new Vector2(pw * 0.26f, by), new Vector2(pw * 0.18f, 2.3f), () => Destroy(gameObject), 0.6f);
            status = Text("", new Vector2(0f, by + 2f), 0.45f, pw - 2f, new Color(1f, 0.85f, 0.35f));
            PadHint("A: CHOOSE   ·   B: CANCEL", new Vector2(0f, by - 1.6f), pw - 2f);
            navKeys = true;
            focus = 0;
        }

        protected override void OnNavBack() => Destroy(gameObject);

        private void OpenIssue()
        {
            Application.OpenURL(IssueUrl(title, body));
            status.text = "OPENED IN YOUR BROWSER: DESCRIBE THE BUG AT THE TOP, THEN SUBMIT";
            Plugin.Log.LogInfo("Bug report: opened a GitHub issue page");
        }

        private void Copy()
        {
            GUIUtility.systemCopyBuffer = title + "\n\n" + body;
            status.text = "COPIED TO YOUR CLIPBOARD";
        }

        private void Update()
        {
            if (NavUpdate()) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Destroy(gameObject);
        }

        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }

        // ------------------------------------------------------------ the report

        public static (string title, string body) Compose()
        {
            var sb = new StringBuilder();
            sb.AppendLine("**What happened?**");
            sb.AppendLine("(describe the bug here: what you did, what you expected, and what happened instead)");
            sb.AppendLine();
            sb.AppendLine("**How to make it happen again** (if you know)");
            sb.AppendLine("1. ");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine($"- Overtime: {Plugin.Version}");
            sb.AppendLine($"- Game: {Application.version} (Unity {Application.unityVersion})");
            sb.AppendLine($"- Mode: {Mode()}");
            sb.AppendLine($"- System: {SystemInfo.operatingSystemFamily}, {Screen.width}x{Screen.height}");
            string log = LogTail(60);
            sb.AppendLine();
            sb.AppendLine("<details><summary>Log (the end of BepInEx/LogOutput.log)</summary>");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(log);
            sb.AppendLine("```");
            sb.AppendLine("</details>");
            return ($"[Bug] Overtime {Plugin.Version}: ", Scrub(sb.ToString()));
        }

        private static string Mode()
        {
            var m = RunState.Mode.ToString();
            if (VersusState.Active) m = $"Versus {VersusState.Setup.Format}, round {VersusState.Round}";
            if (TitleController.onTitle) m = "title screen";
            if (Online.Net.Online) m += $" (online {(Online.Net.IsHost ? "host" : "client")}, {Mathf.Max(1, Online.Net.Roster.Count)} PCs)";
            return m;
        }

        // the newest BepInEx log (two copies of the game write LogOutput.log and LogOutput.log.1), last lines
        private static string LogTail(int lines)
        {
            try
            {
                var dir = BepInEx.Paths.BepInExRootPath;
                var file = new DirectoryInfo(dir).GetFiles("LogOutput.log*").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                if (file == null) return "(no log found)";
                string text;
                using (var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs)) text = sr.ReadToEnd();
                var all = text.Replace("\r", "").Split('\n').Where(l => l.Length > 0).ToList();
                return string.Join("\n", all.Skip(Math.Max(0, all.Count - lines)));
            }
            catch (Exception e) { return "(couldn't read the log: " + e.GetType().Name + ")"; }
        }

        // personal details out: names (this PC's Windows user, Steam name, everyone in the session), paths under the user
        // folder, IP addresses, Steam ids, e-mail addresses
        public static string Scrub(string s)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string n) { if (!string.IsNullOrWhiteSpace(n) && n.Trim().Length >= 2) names.Add(n.Trim()); }
            Add(Environment.UserName);
            Add(Online.Net.MyName);
            foreach (var r in Online.Net.Roster.Values) Add(r.name);
            try { if (Online.SteamTransport.Available) Add(Steamworks.SteamFriends.GetPersonaName()); } catch { }
            s = Regex.Replace(s, @"([A-Za-z]:[\\/]+Users[\\/]+)[^\\/\r\n]+", "$1<user>", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"(/home/|/Users/)[^/\r\n]+", "$1<user>");
            foreach (var n in names.OrderByDescending(n => n.Length)) s = Regex.Replace(s, Regex.Escape(n), "<player>", RegexOptions.IgnoreCase);
            // IP addresses (not version numbers like "BepInEx 5.4.23.5" or "v1.2.3.4")
            s = Regex.Replace(s, @"(?<!(BepInEx |[vV]|version |Unity ))\b\d{1,3}(\.\d{1,3}){3}\b(:\d+)?", "<ip>");
            s = Regex.Replace(s, @"\b7656\d{13}\b", "<steam id>");
            s = Regex.Replace(s, @"[\w.+-]+@[\w-]+\.[\w.]+", "<email>");
            return s;
        }

        // GitHub's new-issue page with title and body filled in; long bodies lose log lines from the top to fit a URL
        public static string IssueUrl(string title, string body)
        {
            string Url(string b) => $"{Repo}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(b)}";
            string url = Url(body);
            while (url.Length > 7800)
            {
                int start = body.IndexOf("```\n", StringComparison.Ordinal);
                int firstLine = start >= 0 ? body.IndexOf('\n', start + 4) : -1;
                if (start < 0 || firstLine < 0) break;
                body = body.Remove(start + 4, firstLine - (start + 4) + 1);
                url = Url(body);
            }
            return url;
        }
    }

    // the pause menu gets REPORT BUG under its last button
    internal static class PauseReport
    {
        private static readonly AccessTools.FieldRef<ButtonController, OnClick> onClickOf = AccessTools.FieldRefAccess<ButtonController, OnClick>("onClick");

        [HarmonyPatch(typeof(PauseController), "Start")]
        private static class AddButton
        {
            [HarmonyPostfix]
            private static void Postfix(PauseController __instance)
            {
                var ui = __instance.pauseUI;
                if (ui == null) return;
                var buttons = ui.GetComponentsInChildren<ButtonController>(true).OrderByDescending(b => b.transform.position.y).ToList();
                if (buttons.Count < 2 || buttons.Any(b => b.name == "Report Bug Button")) return;
                var last = buttons[buttons.Count - 1];
                float step = buttons[buttons.Count - 2].transform.position.y - last.transform.position.y;
                var b = UnityEngine.Object.Instantiate(last.gameObject, last.transform.parent);
                b.name = "Report Bug Button";
                foreach (var m in b.GetComponents<OnClick>()) UnityEngine.Object.DestroyImmediate(m);
                var click = b.AddComponent<PopupClick>();
                click.Action = BugReport.Open;
                var ctrl = b.GetComponent<ButtonController>();
                onClickOf(ctrl) = click;
                MenuPatches.SetButtonLabels(b, "REPORT BUG", "-");
                b.transform.position = last.transform.position - new Vector3(0f, step, 0f);
                var group = last.group;
                if (group != null && !group.buttons.Contains(ctrl)) { ctrl.group = group; group.buttons.Add(ctrl); }
                Plugin.Log.LogInfo($"Pause menu: REPORT BUG added under {last.name}");
            }
        }
    }
}

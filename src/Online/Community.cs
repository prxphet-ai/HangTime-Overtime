using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HangtimeOvertime.Patches;
using UnityEngine;
using UnityEngine.Networking;
using Rules = HangtimeOvertime.Generated.Community;

namespace HangtimeOvertime.Online
{
    // Who this player is to the community server: a random secret kept in HangtimeOvertime_identity.json (only its hash is
    // ever public: the server derives the player id from it), and a display name (the Steam name unless they set one).
    [Serializable]
    public class IdentityData
    {
        public string secret = "";
        public string name = "";
        public bool nameSet;                      // the player chose it (otherwise it follows the Steam name)
    }

    internal static class Identity
    {
        private static IdentityData data;
        private static string id;

        private static string PathOf
        {
            get
            {
                // development: two copies of the game on one PC need two identities (-ovt-profile NAME)
                string profile = null;
                if (Plugin.DevKeys.Value)
                {
                    var args = Environment.GetCommandLineArgs();
                    int i = Array.IndexOf(args, "-ovt-profile");
                    if (i >= 0 && i + 1 < args.Length) profile = Regex.Replace(args[i + 1], "[^A-Za-z0-9]", "");
                }
                return Path.Combine(Application.persistentDataPath, profile == null ? "HangtimeOvertime_identity.json" : $"HangtimeOvertime_identity_{profile}.json");
            }
        }

        private static IdentityData Data
        {
            get
            {
                if (data != null) return data;
                try { data = File.Exists(PathOf) ? JsonUtility.FromJson<IdentityData>(File.ReadAllText(PathOf)) : null; } catch { data = null; }
                if (data == null || !Regex.IsMatch(data.secret ?? "", "^[0-9a-f]{64}$"))
                {
                    var bytes = new byte[32];
                    using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                    data = new IdentityData { secret = string.Concat(bytes.Select(b => b.ToString("x2"))), name = data?.name ?? "", nameSet = data?.nameSet ?? false };
                    Save();
                }
                return data;
            }
        }

        private static void Save()
        {
            try { File.WriteAllText(PathOf, JsonUtility.ToJson(data, true)); }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't save the community identity: " + e.Message); }
        }

        public static string Secret => Data.secret;
        public static string Id => id ?? (id = Sha256(Secret).Substring(0, 20));

        // the name used on the boards: the chosen one, else the Steam name (if it passes the rules), else none yet
        public static string Name
        {
            get
            {
                if (Data.nameSet && NameProblem(Data.name) == null) return Data.name;
                string steam = null;
                try { if (SteamTransport.Available) steam = Steamworks.SteamFriends.GetPersonaName(); } catch { }
                steam = Clean(steam);
                return NameProblem(steam) == null ? steam : "";
            }
        }

        public static bool HasName => Name.Length > 0;

        public static string SetName(string name)
        {
            name = Clean(name);
            var why = NameProblem(name);
            if (why != null) return why;
            Data.name = name; data.nameSet = true;
            Save();
            Community.Post("/v1/player", Community.Json("name", name), (_, __) => { });
            return null;
        }

        private static string Clean(string s) => s == null ? null : Regex.Replace(s, "[\\u0000-\\u001f<>]", "").Trim();

        public static string NameProblem(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < Rules.NameMin || name.Length > Rules.NameMax) return $"NAMES ARE {Rules.NameMin}-{Rules.NameMax} CHARACTERS";
            if (Community.Blocked(name)) return "THAT NAME ISN'T ALLOWED";
            return null;
        }

        public static string Sha256(string text)
        {
            using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(b => b.ToString("x2")));
        }
    }

    // the community server's address and the requests to it (UnityWebRequest; failures come back as an error message)
    internal static class Community
    {
        public static string Server => (Plugin.CommunityServer.Value ?? "").Trim().TrimEnd('/');
        public static bool Configured => Server.StartsWith("http://") || Server.StartsWith("https://");
        public static string LastError;
        public static bool Reachable = true;              // the last request got an answer

        public static void Get(string path, Action<string, string> done) => Runner.Instance.StartCoroutine(Send("GET", path, null, done));

        // a POST: the player's secret is added here (bodies queued for later never hold it)
        public static void Post(string path, string body, Action<string, string> done)
        {
            body = body.TrimEnd().TrimEnd('}');
            if (body.Length > 1) body += ",";
            body += $"\"secret\":\"{Identity.Secret}\"}}";
            Runner.Instance.StartCoroutine(Send("POST", path, body, done));
        }

        private static IEnumerator Send(string method, string path, string body, Action<string, string> done)
        {
            if (!Configured) { done(null, "NOT CONNECTED"); yield break; }
            using (var req = new UnityWebRequest(Server + path, method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                if (body != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    req.SetRequestHeader("content-type", "application/json");
                }
                req.timeout = 10;
                yield return req.SendWebRequest();
                string text = req.downloadHandler?.text;
                if (req.result == UnityWebRequest.Result.ConnectionError || string.IsNullOrEmpty(text))
                {
                    Reachable = false;
                    LastError = "NO CONNECTION TO THE COMMUNITY SERVER";
                    done(null, LastError);
                    yield break;
                }
                Reachable = true;
                var ok = JsonUtility.FromJson<OkReply>(text);
                if (ok == null || !ok.ok) { done(text, (ok?.error ?? req.error ?? "ERROR").ToUpperInvariant()); yield break; }
                done(text, null);
            }
        }

        [Serializable] private class OkReply { public bool ok; public string error; }

        // ------------------------------------------------------------ small JSON writer (flat objects)

        public static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", " ");

        // Json("name", "x", "value", 3.5, "ok", true) -> {"name":"x","value":3.5,"ok":true}
        public static string Json(params object[] kv)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i + 1 < kv.Length; i += 2)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(kv[i]).Append("\":");
                var v = kv[i + 1];
                if (v is string s) sb.Append('"').Append(Esc(s)).Append('"');
                else if (v is bool b) sb.Append(b ? "true" : "false");
                else if (v is float f) sb.Append(f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                else if (v is double d) sb.Append(d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                else sb.Append(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.Append('}').ToString();
        }

        // ------------------------------------------------------------ the word filter (the server checks the same list)

        private static readonly Dictionary<char, char> swaps = new Dictionary<char, char> { { '0', 'o' }, { '1', 'i' }, { '3', 'e' }, { '4', 'a' }, { '5', 's' }, { '7', 't' }, { '@', 'a' }, { '$', 's' } };

        public static bool Blocked(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var t = new string(text.ToLowerInvariant().Select(c => swaps.TryGetValue(c, out var r) ? r : c).ToArray());
            var words = Regex.Split(t, "[^a-z]+").Where(w => w.Length > 0).ToList();
            var squashed = Regex.Replace(t, "[^a-z]", "");
            return Rules.Words.Any(w => w.Length <= 4 ? words.Contains(w) : squashed.Contains(w));
        }

        public static string BoardVersion(string mod) { var m = Regex.Match(mod ?? "", @"^(\d+)\.(\d+)"); return m.Success ? m.Groups[1].Value + "." + m.Groups[2].Value : "0.0"; }
    }
}

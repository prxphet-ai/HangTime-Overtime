using System;
using System.Linq;
using System.Runtime.InteropServices;
using HangtimeOvertime.Core;
using HangtimeOvertime.Online;
using HangtimeOvertime.Patches;
using UnityEngine;

namespace HangtimeOvertime.Debugging
{
    // Development (DevKeys on): two copies of the game on one PC play an online match by themselves.
    //   -ovt-host            opens VERSUS, hosts, and seats this copy's players first (left side first)
    //   -ovt-join            opens VERSUS, joins by IP (Online.JoinAddress), seats its players and readies them
    //   -ovt-auto            the autopilot plays this copy's players (the client's travel as its key presses)
    //   -ovt-format 2v2      the mode (host only; 1v1 by default)
    //   -ovt-seats N         how many players this copy brings (host 1, client 1 by default)
    //   -ovt-pcs N           host: lobby size (2-4 PCs; 2 by default)
    //   -ovt-tag NAME        the window title's suffix (HOST / CLIENT by default)
    //   -ovt-steamcheck      host: 6 s after the lobby opens, search the Steam list and then the lobby's code; log both
    //   -ovt-end N           client: on the match-end screen, step right N times then confirm (1 = REMATCH)
    //   -ovt-fx              host: fires a random perk with visual effects every 3 s (both sides), to compare screens
    //   -ovt-draft           host: the match starts with DRAFT START (each side drafts a team)
    //   -ovt-rules ID        host: plays with that rules preset (sheets/rules.json id; not saved)
    //   -ovt-winfirst N      host: wins its first N matches / rounds 12 s after they start (the game's own scoring)
    //   -ovt-watchdemo       a spectator: steps through the camera views and the builds by itself
    //   -ovt-party           host: opens PARTY CUP once a friend is in, seats key set 1, starts when the friend's device joins
    //   -ovt-chat            in matches, this copy's first player chats every 4 s (quick, emote, typed in turn)
    // The window title gets HOST / CLIENT so screenshots can tell the two apart.
    internal class DevOnline : MonoBehaviour
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr hWnd, string text);

        private static readonly InputKind[] Keys = { InputKind.KeysWASD, InputKind.KeysArrows, InputKind.KeysIJKL, InputKind.KeysNumpad };
        private bool host, join, auto, titled, fx, steamCheck;
        private float steamCheckAt = -1f, codeCheckAt;
        private string tag;
        private float nextFx;
        private int fxPlayed;
        private string format = "1v1";
        private int seats = 1, endRights = 1, pcs = 2;
        private float step = -1f;
        private int stage, seated;
        private float screenSince = -1f;
        private int screenStep;

        public static void Hook(GameObject on)
        {
            var args = Environment.GetCommandLineArgs();
            bool h = Array.IndexOf(args, "-ovt-host") >= 0, j = Array.IndexOf(args, "-ovt-join") >= 0;
            if (!h && !j) return;
            var d = on.AddComponent<DevOnline>();
            Application.runInBackground = true;                        // both copies keep going side by side
            d.host = h; d.join = j; d.auto = Array.IndexOf(args, "-ovt-auto") >= 0; d.fx = Array.IndexOf(args, "-ovt-fx") >= 0;
            string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            d.format = Arg("-ovt-format") ?? "1v1";
            if (int.TryParse(Arg("-ovt-seats"), out var n)) d.seats = Mathf.Clamp(n, 1, 4);
            if (int.TryParse(Arg("-ovt-end"), out var e)) d.endRights = Mathf.Clamp(e, 0, 2);
            if (int.TryParse(Arg("-ovt-pcs"), out var pc)) d.pcs = Mathf.Clamp(pc, 2, 4);
            d.tag = Arg("-ovt-tag");
            d.steamCheck = Array.IndexOf(args, "-ovt-steamcheck") >= 0;
            if (h && Array.IndexOf(args, "-ovt-draft") >= 0) Patches.VersusMenu.DraftStart = true;
            d.chat = Array.IndexOf(args, "-ovt-chat") >= 0;
            d.party = h && Array.IndexOf(args, "-ovt-party") >= 0;
            if (Array.IndexOf(args, "-ovt-watchdemo") >= 0) SpectatorHud.DevDemo = true;
            if (int.TryParse(Arg("-ovt-winfirst"), out var wf)) d.winFirst = wf;
            if (d.party) Patches.BracketPanel.AutoContinue = true;
            var preset = Generated.RulePresets.All.FirstOrDefault(x => x.Id == Arg("-ovt-rules"));
            if (h && preset != null) Core.LobbyRules.Current = Core.RuleSet.From(preset);   // not saved
            Plugin.Log.LogInfo($"Dev online: {(h ? "host" : "client")} {d.format}, {d.seats} seat(s){(d.auto ? ", autopilot" : "")}");
        }

        private static bool DevJoin() { Net.JoinIp(Plugin.OnlineJoin.Value); return true; }

        private static void Press(InputKind k, bool up = false, bool right = false) =>
            Net.OverridePad((int)k, new PadState { Connected = true, Up = up, Confirm = up, Right = right });

        private bool chat, party, partyStarted;
        private int winFirst, wonRound = -1;
        private float roundSince = -1f;
        private float nextChat;
        private int chatN;

        private void Update()
        {
            if (winFirst > 0 && host && MatchSync.InMatch && VersusState.Active && !GameManager.Instance.done)
            {
                if (roundSince < 0f || wonRound == VersusState.Round - 1 && roundSince < 0f) roundSince = Time.unscaledTime;
                if (wonRound != VersusState.Round && Time.unscaledTime - roundSince > 12f)
                {
                    wonRound = VersusState.Round; winFirst--; roundSince = -1f;
                    GameManager.Instance.playerPoints = GameManager.Instance.matchLength;
                    Plugin.Log.LogInfo($"[dev] host wins round {VersusState.Round}");
                }
            }
            else if (!MatchSync.InMatch) roundSince = -1f;
            if (party && !partyStarted && Net.IsHost && Net.HasFriends && VersusMenu.IsOpen)
            {
                if (!PartyMenu.IsOpen) { PartyMenu.Open(); PartyMenu.DevJoin(new SlotInput { Kind = InputKind.KeysWASD, Label = "W A S D" }); }
                else if (PartyMenu.DevCount >= 2) { partyStarted = true; Core.LobbyRules.Current.Rounds = 1; PartyMenu.DevStart(); }
            }
            if (chat && MatchSync.InMatch && Time.unscaledTime > nextChat) { nextChat = Time.unscaledTime + 4f + (host ? 0f : 2f); Patches.ChatHud.DevSay(chatN++); }
            if (!titled && Time.unscaledTime > 3f)
            {
                titled = true;
                var w = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (w != IntPtr.Zero) SetWindowText(w, "Hangtime! " + (tag ?? (host ? "HOST" : "CLIENT")));
            }
            if (auto && (MatchSync.InMatch || VersusOverlay.Picking) && !VersusDriver.Autopilot) VersusDriver.Autopilot = true;
            if (join) ClientScreens();
            if (steamCheck && host && Net.IsHost)
            {
                if (steamCheckAt < 0f) steamCheckAt = Time.unscaledTime + 6f;
                else if (Time.unscaledTime > steamCheckAt && steamCheckAt > 0f)
                {
                    steamCheckAt = 0f;
                    Lobbies.RequestList(true, true, null, list =>
                        Plugin.Log.LogInfo("[steam] list: " + string.Join(" | ", list.Select(l => $"{l.Name} {l.Version} {l.Mode} {l.State} {l.Pcs}/{l.Max} code {l.Code}"))));
                    codeCheckAt = Time.unscaledTime + 4f;
                }
                if (codeCheckAt > 0f && Time.unscaledTime > codeCheckAt)
                {
                    codeCheckAt = 0f;
                    Lobbies.RequestList(true, true, Net.Lobby.Code, byCode =>
                        Plugin.Log.LogInfo($"[steam] code {Net.Lobby.Code}: {byCode.Count} found " + string.Join(" | ", byCode.Select(l => l.Name))));
                }
            }
            if (fx && host && MatchSync.Recording && Time.unscaledTime > nextFx)
            {
                nextFx = Time.unscaledTime + 3f;
                var visual = Core.PerkRegistry.All.Where(p => p.Def.Fx.Any(f => "burst flash aura afterimages bolt ring trail tint invisible decoy zone stun".Contains(f.Kind))).ToList();
                if (visual.Count > 0)
                {
                    var perk = visual[UnityEngine.Random.Range(0, visual.Count)];
                    int side = UnityEngine.Random.Range(0, 2);
                    Engine.Engine.DebugFire(perk, side);
                    Plugin.Log.LogInfo($"[fx] host fired {perk.Def.Title} ({string.Join("+", perk.Def.Fx.Select(f => f.Kind))}) as side {side}");
                }
            }
            if (Time.unscaledTime < step) return;
            switch (stage)
            {
                case 0:                                                    // the title screen is up
                    if (!TitleController.onTitle || Time.unscaledTime < 6f) return;
                    if (host ? VersusMenu.DevHost(format, seats, pcs, steamCheck) : DevJoin()) { stage = 1; step = Time.unscaledTime + 1f; }
                    break;
                case 1:
                    if (host) { stage = 3; break; }
                    if (!Net.Connected) return;
                    step = Time.unscaledTime + 1f;
                    stage = 2;
                    break;
                case 2:                                                    // client: each player joins with its own key set
                    Press(Keys[seated], up: true);
                    seated++;
                    step = Time.unscaledTime + 1f;
                    if (seated >= seats) { stage = 3; seated = 0; }
                    break;
                case 3:                                                    // client: ready each; host: ready once everyone else is
                    if (join)
                    {
                        Press(Keys[seated], up: true);
                        seated++;
                        step = Time.unscaledTime + 0.6f;
                        if (seated >= seats) stage = 4;
                    }
                    else if (!party && VersusMenu.DevReadyWhenOthersAre()) stage = 4;
                    break;
            }
        }

        // the client on the host's screens between rounds: right (pick screen once, end screen endRights times), then
        // jump, from every key set it plays on; the host only listens to whoever's turn it is
        private int seenSerial;

        private void ClientScreens()
        {
            if (!VersusOverlay.IsMirror) { screenSince = -1f; return; }
            if (screenSince < 0f || seenSerial != VersusOverlay.MirrorSerial) { screenSince = Time.unscaledTime; screenStep = 0; seenSerial = VersusOverlay.MirrorSerial; }
            int rights = VersusOverlay.MirrorMatchOver ? endRights : 1;
            float t = Time.unscaledTime - screenSince;
            if (screenStep < rights && t > 1f + screenStep * 0.6f) { for (int i = 0; i < seats; i++) Press(Keys[i], right: true); screenStep++; }
            else if (screenStep == rights && t > 1.6f + rights * 0.6f) { for (int i = 0; i < seats; i++) Press(Keys[i], up: true); screenStep++; }
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HangtimeOvertime.Patches;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HangtimeOvertime.Online
{
    // Keeps every friend's PC on the host's screen: the lobby (seats, chat, players), each round's load, the screens
    // between rounds, notices, the way back to the lobby after a match, and the way out when the session ends.
    internal static class OnlineFlow
    {
        public static PlayerLook HostLook;             // a friend's PC: the host's player 1 look
        public static readonly List<string> Chat = new List<string>();
        public static event System.Action ChatChanged;
        public static string PendingMessage;            // shown by the lobby browser next time it opens (why a session ended)

        public static void Init()
        {
            Net.On(Msg.Menu, r => VersusMenu.MirrorState(r));
            Net.On(Msg.Load, OnLoad);
            Net.On(Msg.Ready, (r, p) => { p.ReadySeq = r.I(); Plugin.Log.LogInfo($"Online: {p.Name} ready for load {p.ReadySeq}"); });
            Net.On(Msg.Snap, r => { if (r.I() == MatchSync.LoadSeq) Mirror.Current?.OnSnap(r); });
            Net.On(Msg.Events, r => { if (r.I() == MatchSync.LoadSeq) Mirror.Current?.OnEvents(r); });
            Net.On(Msg.Screen, r => { if (Net.IsClient) VersusOverlay.ShowMirror(RoundScreen.Read(r)); });
            Net.On(Msg.ScreenSel, r => VersusOverlay.MirrorSelection(r.I(), r.I()));
            Net.On(Msg.ScreenClose, r => VersusOverlay.Close());
            Net.On(Msg.Notice, r => { if (Net.IsClient) { string t = r.S(); VersusOverlay.Notice(t.Length == 0 ? null : t); } });
            Net.On(Msg.Chat, (r, p) =>
            {
                if (Net.IsHost) Relay(p.Name, r.S());                                  // a friend's line: to everyone
                else AddLine(r.S());                                                     // the host's relay: name included
            });
            Net.On(Msg.ToLobby, r => { if (Net.IsClient) GoToLobby(); });
            Net.OnClosed += OnClosed;
            Net.OnPeerJoined += p =>
            {
                // the chat so far (its last line, "... JOINED", already went to everyone)
                foreach (var line in Chat.Take(Mathf.Max(0, Chat.Count - 1)).Skip(Mathf.Max(0, Chat.Count - 13))) Net.SendTo(p, new NetWriter(Msg.Chat).S(line));
                VersusMenu.PeerJoined();
            };
            Net.OnPeerLeft += p => VersusMenu.PeerLeft(p.Id);
        }

        private static bool HostLive => Net.IsHost && Net.HasFriends;
        public static bool MatchRunning => VersusState.Active;

        // ------------------------------------------------------------ chat

        public static void Say(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0 || !Net.Online) return;
            if (text.Length > 120) text = text.Substring(0, 120);
            if (Net.IsHost) Relay(Net.MyName, text);
            else Net.Send(new NetWriter(Msg.Chat).S(text));
        }

        private static void Relay(string name, string text)
        {
            string line = $"<color=#FFD95A>{Clean(name)}</color>: {Clean(text)}";
            AddLine(line);
            Net.Send(new NetWriter(Msg.Chat).S(line));
        }

        public static void SystemChat(string text)
        {
            if (!Net.IsHost) return;
            string line = $"<color=#8FD3FF>{Clean(text)}</color>";
            AddLine(line);
            Net.Send(new NetWriter(Msg.Chat).S(line));
        }

        // no rich-text tags from players
        private static string Clean(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");

        private static void AddLine(string line)
        {
            Chat.Add(line);
            while (Chat.Count > 40) Chat.RemoveAt(0);
            ChatChanged?.Invoke();
        }

        // ------------------------------------------------------------ host -> friends

        public static void SendScreen(RoundScreen d)
        {
            if (!HostLive) return;
            var w = new NetWriter(Msg.Screen);
            d.Write(w);
            Net.Send(w);
        }

        private static (int, int) lastSel = (-1, -1);
        public static void SendSelection(int sel, int endSel)
        {
            if (!HostLive || lastSel == (sel, endSel)) return;
            lastSel = (sel, endSel);
            Net.Send(new NetWriter(Msg.ScreenSel).I(sel).I(endSel));
        }

        public static void SendNotice(string text)
        {
            if (HostLive) Net.Send(new NetWriter(Msg.Notice).S(text ?? ""));
        }

        // every friend's PC has finished loading this round
        public static bool AllReady(int seq) => Net.Peers.Where(p => p.Welcomed).All(p => p.ReadySeq == seq);

        // the next round: everything a friend's PC needs to build the same scene
        public static void SendLoad(bool first)
        {
            if (!Net.IsHost) return;
            MatchSync.LoadSeq++;
            lastSel = (-1, -1);
            if (first) Lobbies.SetInMatch(true);
            if (!HostLive) return;
            var s = VersusState.Setup;
            var w = new NetWriter(Msg.Load).I(MatchSync.LoadSeq).B(first).S(s.Format).U8(s.Slots.Count);
            foreach (var x in s.Slots)
                w.U8(x.Side).U8(x.Number).S(x.Role).U8((int)x.Control).U8((int)(x.Input?.Kind ?? InputKind.None)).I(x.Input?.DeviceId ?? 0).S(x.Input?.Label ?? "");
            w.S(s.Team[0]).S(s.Team[1]).I(VersusState.Round).I(VersusState.RoundWins[0]).I(VersusState.RoundWins[1])
             .I(VersusState.Target).I(VersusState.Picks[0]).I(VersusState.Picks[1]).U8(GameManager.Instance.numberOfPlayers)
             .S(JsonUtility.ToJson(Look.Current));
            Net.Send(w);
            Plugin.Log.LogInfo($"Online: sent load {MatchSync.LoadSeq} (round {VersusState.Round}{(first ? ", new match" : "")})");
        }

        // the match-end MENU online: everyone back to the lobby screen, the session stays
        public static void BackToLobby()
        {
            if (Net.IsHost) { Net.Send(new NetWriter(Msg.ToLobby)); Lobbies.SetInMatch(false); }
            GoToLobby();
        }

        private static void GoToLobby()
        {
            Plugin.Log.LogInfo("Online: back to the lobby");
            VersusState.Leave(keepSession: true);
            Runner.Instance.StartCoroutine(ToTitle());
        }

        private static IEnumerator ToTitle()
        {
            var gm = GameManager.Instance;
            gm.transition.Play();
            yield return new WaitForSecondsRealtime(0.5f);
            VersusOverlay.Close();
            VersusOverlay.Notice(null);
            MatchSync.InMatch = false;
            Time.timeScale = 1f;
            gm.ResetGame();
            PauseController.paused = false;
            GameManager.gameOver = true;
            GameManager.gameNumber = 0;
            TitleController.onTitle = true;
            SceneManager.LoadScene("Game");
        }

        // ------------------------------------------------------------ a friend's PC

        private static void OnLoad(NetReader r)
        {
            if (!Net.IsClient) return;
            int seq = r.I(); bool first = r.B();
            var setup = new VersusSetup { Format = r.S() };
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                var slot = new VersusSlot { Side = r.U8(), Number = r.U8(), Role = r.S(), Control = (SlotControl)r.U8() };
                var kind = (InputKind)r.U8(); int dev = r.I(); string label = r.S();
                if (kind != InputKind.None) slot.Input = new SlotInput { Kind = kind, DeviceId = dev, Label = label };
                setup.Slots.Add(slot);
            }
            setup.Team[0] = r.S(); setup.Team[1] = r.S();
            int round = r.I(), w0 = r.I(), w1 = r.I(), target = r.I(), p0 = r.I(), p1 = r.I(), players = r.U8();
            try { HostLook = JsonUtility.FromJson<PlayerLook>(r.S()); } catch { HostLook = null; }

            if (first || VersusState.Setup == null) VersusState.NewMatch(setup);
            else VersusState.Setup = setup;
            VersusState.Round = round; VersusState.RoundWins[0] = w0; VersusState.RoundWins[1] = w1;
            VersusState.Target = target; VersusState.Picks[0] = p0; VersusState.Picks[1] = p1;
            MatchSync.LoadSeq = seq;
            MatchSync.InMatch = false;
            GameManager.Instance.numberOfPlayers = players;
            Plugin.Log.LogInfo($"Online: loading round {round} (load {seq})");
            if (first && VersusMenu.StartFromHost()) return;
            VersusFlow.ReloadRound();
        }

        // the session ended (any reason): a friend's PC in a match goes back to the title; the lobby screen closes
        private static void OnClosed(string reason)
        {
            MatchSync.InMatch = false;
            PendingMessage = reason;
            if (!VersusState.Active) { VersusMenu.SessionEnded(reason); return; }
            if (Mirror.Current != null || VersusOverlay.IsMirror) VersusFlow.ToMenu();
            // a host in a match: the friends' players read as disconnected, so the match pauses with a notice
        }

        // ------------------------------------------------------------ the title screen

        // a Steam invite was accepted (open the browser and join), a session is waiting for its lobby screen (just
        // joined, or back from a match), or a session ended (the browser says why)
        public static void TitleTick()
        {
            if (!TitleController.onTitle || VersusMenu.IsOpen || LobbyBrowser.IsOpen || !VersusMenu.TitleReady) return;
            if (Lobbies.PendingLobby.IsValid()) { LobbyBrowser.Open(); Lobbies.TakePending(); return; }
            if (Net.Online && Net.Connected && !VersusState.Active) { VersusMenu.OpenLobby(); return; }
            if (PendingMessage != null && !Net.Online) LobbyBrowser.Open();
        }
    }
}

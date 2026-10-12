using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HangtimeOvertime.Patches;
using UnityEngine;

namespace HangtimeOvertime.Online
{
    // Keeps the client on the host's screen: the join screen, each round's load, the screens between rounds, notices,
    // and the way out when the session ends.
    internal static class OnlineFlow
    {
        public static PlayerLook HostLook;             // client: the host's player 1 look
        public static int PeerReadySeq = -1;           // host: the round load the client has finished

        public static void Init()
        {
            Net.On(Msg.Menu, r => VersusMenu.MirrorState(r));
            Net.On(Msg.Load, OnLoad);
            Net.On(Msg.Ready, r => { PeerReadySeq = r.I(); Plugin.Log.LogInfo($"Online: {Net.PeerName} ready for load {PeerReadySeq}"); });
            Net.On(Msg.Snap, r => { if (r.I() == MatchSync.LoadSeq) Mirror.Current?.OnSnap(r); });
            Net.On(Msg.Events, r => { if (r.I() == MatchSync.LoadSeq) Mirror.Current?.OnEvents(r); });
            Net.On(Msg.Screen, r => { if (Net.IsClient) VersusOverlay.ShowMirror(RoundScreen.Read(r)); });
            Net.On(Msg.ScreenSel, r => VersusOverlay.MirrorSelection(r.I(), r.I()));
            Net.On(Msg.ScreenClose, r => VersusOverlay.Close());
            Net.On(Msg.Notice, r => { if (Net.IsClient) { string t = r.S(); VersusOverlay.Notice(t.Length == 0 ? null : t); } });
            Net.On(Msg.Hello, r => VersusMenu.PeerJoined());
            Net.OnClosed += OnClosed;
        }

        private static bool HostLive => Net.IsHost && Net.Connected;

        // ------------------------------------------------------------ host -> client

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

        // the next round: everything the client needs to build the same scene
        public static void SendLoad(bool first)
        {
            if (!HostLive) return;
            MatchSync.LoadSeq++;
            lastSel = (-1, -1);
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

        // ------------------------------------------------------------ client

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

        // the session ended (either side, any reason)
        private static void OnClosed(string reason)
        {
            MatchSync.InMatch = false;
            if (!VersusState.Active) { VersusMenu.SessionEnded(reason); return; }
            if (Mirror.Current != null || VersusOverlay.IsMirror)
            {
                // a client in a match: back to the title with the reason
                VersusFlow.ToMenu();
                VersusMenu.PendingMessage = reason;
            }
            // a host in a match: the remote players' inputs read as disconnected, so the match pauses with a notice
        }
    }
}

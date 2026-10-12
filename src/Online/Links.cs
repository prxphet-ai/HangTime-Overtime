using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Steamworks;
using UnityEngine;

namespace HangtimeOvertime.Online
{
    // A connection to the one other machine in an online match. Messages are whole byte arrays; reliable ones arrive
    // once and in order, unreliable ones may be lost (used for the 50-a-second snapshots and inputs, where only the
    // newest matters).
    internal interface INetLink
    {
        bool HasPeer { get; }                 // the host: someone has connected; the client: always (it dials the host)
        string PeerName { get; }
        void Send(byte[] msg, bool reliable);
        void Poll(List<byte[]> received);     // appends everything that arrived since the last poll
        void Close();
    }

    // ------------------------------------------------------------ direct IP (UDP), with a small reliable channel

    // Datagram: [kind:1] then
    //   kind 0 unreliable: payload
    //   kind 1 reliable:   [seq:4] payload            (resent until acknowledged, delivered in order)
    //   kind 2 ack:        [next expected seq:4]       (everything before it arrived)
    internal class UdpLink : INetLink
    {
        private readonly UdpClient udp;
        private IPEndPoint peer;
        private readonly bool isHost;
        private uint nextSendSeq, nextRecvSeq;
        private readonly SortedDictionary<uint, (byte[] data, float sentAt)> unacked = new SortedDictionary<uint, (byte[], float)>();
        private readonly Dictionary<uint, byte[]> early = new Dictionary<uint, byte[]>();
        private const float Resend = 0.12f;

        public bool HasPeer => peer != null;
        public string PeerName => peer != null ? peer.ToString() : "";

        private UdpLink(UdpClient udp, IPEndPoint peer, bool isHost)
        {
            this.udp = udp;
            this.peer = peer;
            this.isHost = isHost;
            udp.Client.Blocking = false;
            // Windows reports "port unreachable" from an earlier send as an error on the next receive; ignore it
            try { udp.Client.IOControl(-1744830452, new byte[] { 0 }, null); } catch { }
        }

        public static UdpLink Listen(string bind, int port) =>
            new UdpLink(new UdpClient(new IPEndPoint(IPAddress.Parse(bind), port)), null, true);

        public static UdpLink Dial(string host, int port)
        {
            var addr = Dns.GetHostAddresses(host);
            var ip = Array.Find(addr, a => a.AddressFamily == AddressFamily.InterNetwork) ?? addr[0];
            return new UdpLink(new UdpClient(new IPEndPoint(IPAddress.Any, 0)), new IPEndPoint(ip, port), false);
        }

        public void Send(byte[] msg, bool reliable)
        {
            if (peer == null) return;
            if (!reliable) { Raw(Frame(0, 0, msg, false)); return; }
            uint seq = nextSendSeq++;
            var d = Frame(1, seq, msg, true);
            unacked[seq] = (d, Time.unscaledTime);
            Raw(d);
        }

        private static byte[] Frame(byte kind, uint seq, byte[] msg, bool withSeq)
        {
            var d = new byte[1 + (withSeq ? 4 : 0) + msg.Length];
            d[0] = kind;
            int o = 1;
            if (withSeq) { BitConverter.GetBytes(seq).CopyTo(d, 1); o = 5; }
            Buffer.BlockCopy(msg, 0, d, o, msg.Length);
            return d;
        }

        private void Raw(byte[] d)
        {
            try { udp.Send(d, d.Length, peer); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        public void Poll(List<byte[]> received)
        {
            while (true)
            {
                byte[] d;
                IPEndPoint from = null;
                try
                {
                    if (udp.Available <= 0) break;
                    d = udp.Receive(ref from);
                }
                catch (SocketException) { break; }
                catch (ObjectDisposedException) { return; }
                if (d.Length < 1) continue;
                if (peer == null && isHost) peer = from;                 // the first datagram picks the peer
                if (!from.Equals(peer)) continue;                        // one peer per match
                switch (d[0])
                {
                    case 0:
                        received.Add(Sub(d, 1));
                        break;
                    case 1:
                        if (d.Length < 5) break;
                        uint seq = BitConverter.ToUInt32(d, 1);
                        if (seq >= nextRecvSeq && !early.ContainsKey(seq)) early[seq] = Sub(d, 5);
                        while (early.TryGetValue(nextRecvSeq, out var m)) { early.Remove(nextRecvSeq); received.Add(m); nextRecvSeq++; }
                        var ack = new byte[5]; ack[0] = 2; BitConverter.GetBytes(nextRecvSeq).CopyTo(ack, 1);
                        Raw(ack);
                        break;
                    case 2:
                        if (d.Length < 5) break;
                        uint upTo = BitConverter.ToUInt32(d, 1);
                        var done = new List<uint>();
                        foreach (var k in unacked.Keys) { if (k < upTo) done.Add(k); else break; }
                        foreach (var k in done) unacked.Remove(k);
                        break;
                }
            }
            // resend what hasn't been acknowledged (oldest first, a few per frame)
            float now = Time.unscaledTime;
            int n = 0;
            var keys = new List<uint>(unacked.Keys);
            foreach (var k in keys)
            {
                var u = unacked[k];
                if (now - u.sentAt < Resend) continue;
                Raw(u.data);
                unacked[k] = (u.data, now);
                if (++n >= 16) break;
            }
        }

        private static byte[] Sub(byte[] d, int from)
        {
            var r = new byte[d.Length - from];
            Buffer.BlockCopy(d, from, r, 0, r.Length);
            return r;
        }

        public void Close() { try { udp.Close(); } catch { } }
    }

    // ------------------------------------------------------------ Steam (friends lobby + Steam's own P2P relay)

    // The host makes a friends-only lobby and opens Steam's invite dialog; a friend who accepts (or picks "Join game" in
    // their friends list) enters the lobby and messages the lobby owner. Steam relays the traffic, so no ports or IPs.
    internal class SteamLink : INetLink
    {
        private const int Channel = 0;
        private SteamNetworkingIdentity peer;
        private bool hasPeer;
        private readonly bool isHost;
        private readonly IntPtr[] inbox = new IntPtr[64];
        private Callback<SteamNetworkingMessagesSessionRequest_t> onRequest;

        public bool HasPeer => hasPeer;
        public string PeerName { get; private set; } = "";
        public CSteamID Lobby;

        public static bool Available
        {
            get { try { return SteamManager.Initialized; } catch { return false; } }
        }

        private SteamLink(bool isHost)
        {
            this.isHost = isHost;
            onRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(r =>
            {
                // the host takes the first friend who messages it; the client only ever hears from its host
                var id = r.m_identityRemote;
                if (isHost && !hasPeer) SetPeer(id.GetSteamID());
                if (hasPeer && id.GetSteamID() == peer.GetSteamID()) SteamNetworkingMessages.AcceptSessionWithUser(ref id);
            });
        }

        public static SteamLink Host() => new SteamLink(true);

        public static SteamLink Dial(CSteamID host)
        {
            var l = new SteamLink(false);
            l.SetPeer(host);
            return l;
        }

        private void SetPeer(CSteamID id)
        {
            peer = new SteamNetworkingIdentity();
            peer.SetSteamID(id);
            hasPeer = true;
            try { PeerName = SteamFriends.GetFriendPersonaName(id); } catch { PeerName = id.ToString(); }
        }

        public void Send(byte[] msg, bool reliable)
        {
            if (!hasPeer) return;
            int flags = (reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_UnreliableNoDelay)
                        | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession;
            var h = GCHandle.Alloc(msg, GCHandleType.Pinned);
            try { SteamNetworkingMessages.SendMessageToUser(ref peer, h.AddrOfPinnedObject(), (uint)msg.Length, flags, Channel); }
            finally { h.Free(); }
        }

        public void Poll(List<byte[]> received)
        {
            int n = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, inbox, inbox.Length);
            for (int i = 0; i < n; i++)
            {
                var m = SteamNetworkingMessage_t.FromIntPtr(inbox[i]);
                var from = m.m_identityPeer.GetSteamID();
                if (isHost && !hasPeer) SetPeer(from);
                if (hasPeer && from == peer.GetSteamID())
                {
                    var d = new byte[m.m_cbSize];
                    Marshal.Copy(m.m_pData, d, 0, m.m_cbSize);
                    received.Add(d);
                }
                SteamNetworkingMessage_t.Release(inbox[i]);
            }
        }

        public void Close()
        {
            if (hasPeer) { var p = peer; SteamNetworkingMessages.CloseSessionWithUser(ref p); }
            if (Lobby.IsValid()) SteamMatchmaking.LeaveLobby(Lobby);
            onRequest?.Dispose();
            onRequest = null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Steamworks;
using UnityEngine;

namespace HangtimeOvertime.Online
{
    // A way to reach other machines. The host's transports hold up to three peers; a client's holds one (the host).
    // Peers are numbered by the transport. Messages are whole byte arrays; reliable ones arrive once and in order,
    // unreliable ones may be lost (snapshots and inputs, where only the newest matters).
    internal interface ITransport
    {
        string Kind { get; }                                   // "direct IP" / "Steam"
        void Send(int peer, byte[] msg, bool reliable);
        void Poll(List<(int peer, byte[] data)> received);     // appends everything that arrived since the last poll
        void Drop(int peer);                                   // forget a peer (it left or was refused)
        void Close();
    }

    // ------------------------------------------------------------ direct IP (UDP), with a small reliable channel per peer

    // Datagram: [kind:1] then
    //   kind 0 unreliable: payload
    //   kind 1 reliable:   [seq:4] payload            (resent until acknowledged, delivered in order)
    //   kind 2 ack:        [next expected seq:4]       (everything before it arrived)
    internal class UdpTransport : ITransport
    {
        private class Channel
        {
            public IPEndPoint End;
            public uint NextSend, NextRecv;
            public readonly SortedDictionary<uint, (byte[] data, float sentAt)> Unacked = new SortedDictionary<uint, (byte[], float)>();
            public readonly Dictionary<uint, byte[]> Early = new Dictionary<uint, byte[]>();
        }

        private readonly UdpClient udp;
        private readonly bool accepting;                       // the host takes new peers; a client only talks to its host
        private readonly Dictionary<int, Channel> peers = new Dictionary<int, Channel>();
        private readonly Dictionary<string, int> byEnd = new Dictionary<string, int>();
        private int nextId = 1;
        private const float Resend = 0.12f;

        public string Kind => "direct IP";

        private UdpTransport(UdpClient udp, bool accepting)
        {
            this.udp = udp;
            this.accepting = accepting;
            udp.Client.Blocking = false;
            // Windows reports "port unreachable" from an earlier send as an error on the next receive; ignore it
            try { udp.Client.IOControl(-1744830452, new byte[] { 0 }, null); } catch { }
        }

        public static UdpTransport Listen(string bind, int port) => new UdpTransport(new UdpClient(new IPEndPoint(IPAddress.Parse(bind), port)), true);

        // a client: peer 0 is the host
        public static UdpTransport Dial(string host, int port)
        {
            var addr = Dns.GetHostAddresses(host);
            var ip = Array.Find(addr, a => a.AddressFamily == AddressFamily.InterNetwork) ?? addr[0];
            var t = new UdpTransport(new UdpClient(new IPEndPoint(IPAddress.Any, 0)), false);
            t.Add(0, new IPEndPoint(ip, port));
            return t;
        }

        private Channel Add(int id, IPEndPoint end)
        {
            var c = new Channel { End = end };
            peers[id] = c;
            byEnd[end.ToString()] = id;
            return c;
        }

        public void Send(int peer, byte[] msg, bool reliable)
        {
            if (!peers.TryGetValue(peer, out var c)) return;
            if (!reliable) { Raw(c, Frame(0, 0, msg, false)); return; }
            uint seq = c.NextSend++;
            var d = Frame(1, seq, msg, true);
            c.Unacked[seq] = (d, Time.unscaledTime);
            Raw(c, d);
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

        private void Raw(Channel c, byte[] d)
        {
            try { udp.Send(d, d.Length, c.End); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        public void Poll(List<(int peer, byte[] data)> received)
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
                if (!byEnd.TryGetValue(from.ToString(), out int id))
                {
                    if (!accepting) continue;                            // a client only listens to its host
                    id = nextId++;
                    Add(id, from);
                }
                var c = peers[id];
                switch (d[0])
                {
                    case 0:
                        received.Add((id, Sub(d, 1)));
                        break;
                    case 1:
                        if (d.Length < 5) break;
                        uint seq = BitConverter.ToUInt32(d, 1);
                        if (seq >= c.NextRecv && !c.Early.ContainsKey(seq)) c.Early[seq] = Sub(d, 5);
                        while (c.Early.TryGetValue(c.NextRecv, out var m)) { c.Early.Remove(c.NextRecv); received.Add((id, m)); c.NextRecv++; }
                        var ack = new byte[5]; ack[0] = 2; BitConverter.GetBytes(c.NextRecv).CopyTo(ack, 1);
                        Raw(c, ack);
                        break;
                    case 2:
                        if (d.Length < 5) break;
                        uint upTo = BitConverter.ToUInt32(d, 1);
                        var done = new List<uint>();
                        foreach (var k in c.Unacked.Keys) { if (k < upTo) done.Add(k); else break; }
                        foreach (var k in done) c.Unacked.Remove(k);
                        break;
                }
            }
            // resend what hasn't been acknowledged (oldest first, a few per peer per frame)
            float now = Time.unscaledTime;
            foreach (var c in peers.Values)
            {
                int n = 0;
                foreach (var k in new List<uint>(c.Unacked.Keys))
                {
                    var u = c.Unacked[k];
                    if (now - u.sentAt < Resend) continue;
                    Raw(c, u.data);
                    c.Unacked[k] = (u.data, now);
                    if (++n >= 16) break;
                }
            }
        }

        private static byte[] Sub(byte[] d, int from)
        {
            var r = new byte[d.Length - from];
            Buffer.BlockCopy(d, from, r, 0, r.Length);
            return r;
        }

        public void Drop(int peer)
        {
            if (!peers.TryGetValue(peer, out var c)) return;
            peers.Remove(peer);
            byEnd.Remove(c.End.ToString());
        }

        public void Close() { try { udp.Close(); } catch { } }
    }

    // ------------------------------------------------------------ Steam (Steam's own P2P relay: no ports, no addresses)

    internal class SteamTransport : ITransport
    {
        private const int Channel = 0;
        private readonly bool accepting;
        private readonly Dictionary<int, SteamNetworkingIdentity> peers = new Dictionary<int, SteamNetworkingIdentity>();
        private readonly Dictionary<ulong, int> bySteam = new Dictionary<ulong, int>();
        private int nextId = 1;
        private readonly IntPtr[] inbox = new IntPtr[64];
        private Callback<SteamNetworkingMessagesSessionRequest_t> onRequest;

        public string Kind => "Steam";

        public static bool Available
        {
            get { try { return SteamManager.Initialized; } catch { return false; } }
        }

        private SteamTransport(bool accepting)
        {
            this.accepting = accepting;
            onRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(r =>
            {
                var id = r.m_identityRemote;
                ulong sid = id.GetSteamID64();
                if (bySteam.ContainsKey(sid) || accepting) SteamNetworkingMessages.AcceptSessionWithUser(ref id);
            });
        }

        public static SteamTransport Host() => new SteamTransport(true);

        // a client: peer 0 is the host
        public static SteamTransport Dial(CSteamID host)
        {
            var t = new SteamTransport(false);
            t.Add(0, host);
            return t;
        }

        private int Add(int id, CSteamID sid)
        {
            var ident = new SteamNetworkingIdentity();
            ident.SetSteamID(sid);
            peers[id] = ident;
            bySteam[sid.m_SteamID] = id;
            return id;
        }

        public static string NameOf(CSteamID id)
        {
            try { return SteamFriends.GetFriendPersonaName(id); } catch { return id.ToString(); }
        }

        public void Send(int peer, byte[] msg, bool reliable)
        {
            if (!peers.TryGetValue(peer, out var ident)) return;
            int flags = (reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_UnreliableNoDelay)
                        | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession;
            var h = GCHandle.Alloc(msg, GCHandleType.Pinned);
            try { SteamNetworkingMessages.SendMessageToUser(ref ident, h.AddrOfPinnedObject(), (uint)msg.Length, flags, Channel); }
            finally { h.Free(); }
        }

        public void Poll(List<(int peer, byte[] data)> received)
        {
            int n = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, inbox, inbox.Length);
            for (int i = 0; i < n; i++)
            {
                var m = SteamNetworkingMessage_t.FromIntPtr(inbox[i]);
                var from = m.m_identityPeer.GetSteamID();
                if (!bySteam.TryGetValue(from.m_SteamID, out int id) && accepting) id = Add(nextId++, from);
                if (bySteam.ContainsKey(from.m_SteamID))
                {
                    var d = new byte[m.m_cbSize];
                    Marshal.Copy(m.m_pData, d, 0, m.m_cbSize);
                    received.Add((id, d));
                }
                SteamNetworkingMessage_t.Release(inbox[i]);
            }
        }

        public void Drop(int peer)
        {
            if (!peers.TryGetValue(peer, out var ident)) return;
            SteamNetworkingMessages.CloseSessionWithUser(ref ident);
            bySteam.Remove(ident.GetSteamID64());
            peers.Remove(peer);
        }

        public void Close()
        {
            foreach (var p in new List<int>(peers.Keys)) Drop(p);
            onRequest?.Dispose();
            onRequest = null;
        }
    }
}

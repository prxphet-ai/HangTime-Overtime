using System;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Patches;
using Steamworks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HangtimeOvertime.Online
{
    internal enum NetRole { None, Host, Client }

    // Another PC in the session. The host has one per friend (up to three); a client has one, the host.
    internal class Peer
    {
        public ITransport T;
        public int Tid;                    // the transport's number for it
        public int Id;                     // the session's: the host is 0, friends 1-3 (in their device keys and labels)
        public string Name = "";
        public bool Welcomed;
        public float LastHeard, PingMs;
        public int ReadySeq = -1;          // the round load it has finished
        public bool Spectator;             // watches only: its devices take no seat, the host doesn't wait for it
    }

    internal class LobbyOptions
    {
        public string Name = "";
        public bool Public = true;
        public int MaxPCs = 2;
        public string Code = "";
    }

    // The online session. The host runs the real match; up to three friends' PCs mirror it and send their players'
    // inputs. Hosting listens on Steam (a lobby others find in the browser, by code or by invite) and on direct IP.
    internal static class Net
    {
        public const int Protocol = 8;               // 4: the match-end screen names the winner (streaks); 5: in-match chat, draft start; 6: lobby rules; 7: party cup; 8: spectators

        public static NetRole Role { get; private set; }
        public static bool IsHost => Role == NetRole.Host;
        public static bool IsClient => Role == NetRole.Client;
        public static bool Online => Role != NetRole.None;
        // host: hosting (friends may come and go); client: welcomed by the host
        public static bool Connected => IsHost || (IsClient && HostPeer != null && HostPeer.Welcomed);
        public static bool HasFriends => IsHost && Peers.Any(p => p.Welcomed);
        public static int MyId { get; private set; }  // 0 = host
        public static bool ImSpectator;                // a friend's PC: this PC only watches
        public static string MyName { get; private set; } = "";
        public static string Status = "";             // one line for the menus
        public static readonly LobbyOptions Lobby = new LobbyOptions();
        public static float PingMs => IsClient && HostPeer != null ? HostPeer.PingMs : 0f;
        public static event Action<string> OnClosed;   // reason
        public static event Action<Peer> OnPeerLeft;   // host: a friend left or dropped
        public static event Action<Peer> OnPeerJoined;

        public static readonly List<Peer> Peers = new List<Peer>();   // host: every friend (welcomed or saying hello)
        public static Peer HostPeer;                                    // client
        // the session's name list: id -> (name, ping), the host's own included; the host sends it round
        public static readonly SortedDictionary<int, (string name, int ping)> Roster = new SortedDictionary<int, (string, int)>();

        private static UdpTransport udp;
        private static SteamTransport steam;
        private static float lastPing, lastHello, startedAt, lastRoster;
        private static readonly Dictionary<Msg, Action<NetReader, Peer>> handlers = new Dictionary<Msg, Action<NetReader, Peer>>();
        private static readonly List<(int peer, byte[] data)> inbox = new List<(int, byte[])>();

        public static void On(Msg id, Action<NetReader> h) => handlers[id] = (r, p) => h(r);
        public static void On(Msg id, Action<NetReader, Peer> h) => handlers[id] = h;

        // host: to every welcomed friend; client: to the host
        public static void Send(NetWriter w, bool reliable = true)
        {
            var bytes = w.Bytes();
            if (IsClient) { if (HostPeer != null) HostPeer.T.Send(HostPeer.Tid, bytes, reliable); return; }
            foreach (var p in Peers) if (p.Welcomed) p.T.Send(p.Tid, bytes, reliable);
        }

        public static void SendTo(Peer p, NetWriter w, bool reliable = true) => p?.T.Send(p.Tid, w.Bytes(), reliable);

        private static string LocalName()
        {
            try { if (SteamTransport.Available) return SteamFriends.GetPersonaName(); } catch { }
            return Environment.UserName;
        }

        // ------------------------------------------------------------ starting and stopping

        // an online game keeps running while its window is in the background (alt-tab must not freeze anyone else)
        private static bool savedRunInBackground;
        private static void Begin(NetRole role)
        {
            Stop(null, false);
            savedRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            Role = role;
            MyName = LocalName();
            startedAt = Time.unscaledTime;
        }

        public static void StartHost(LobbyOptions opts)
        {
            Begin(NetRole.Host);
            MyId = 0;
            Lobby.Name = string.IsNullOrWhiteSpace(opts.Name) ? MyName + "'s lobby" : opts.Name.Trim();
            Lobby.Public = opts.Public;
            Lobby.MaxPCs = Mathf.Clamp(opts.MaxPCs, 2, 4);
            Lobby.Code = Lobbies.NewCode();
            var parts = new List<string>();
            try
            {
                udp = UdpTransport.Listen(Plugin.OnlineBind.Value, Plugin.OnlinePort.Value);
                parts.Add($"DIRECT IP PORT {Plugin.OnlinePort.Value}");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Online: direct IP unavailable: " + e.Message); }
            if (SteamTransport.Available)
            {
                steam = SteamTransport.Host();
                Lobbies.Create(Lobby);
                parts.Add("STEAM");
            }
            Status = parts.Count == 0 ? "COULDN'T START HOSTING" : "LOBBY OPEN (" + string.Join(" · ", parts) + ")";
            RefreshRoster();
            Plugin.Log.LogInfo($"Online: hosting '{Lobby.Name}' code {Lobby.Code}, {(Lobby.Public ? "public" : "private")}, up to {Lobby.MaxPCs} PCs via " + string.Join(", ", parts));
        }

        public static void JoinIp(string address)
        {
            Begin(NetRole.Client);
            try
            {
                string host = address.Trim(); int port = Plugin.OnlinePort.Value;
                int colon = host.LastIndexOf(':');
                if (colon > 0 && int.TryParse(host.Substring(colon + 1), out var p)) { port = p; host = host.Substring(0, colon); }
                udp = UdpTransport.Dial(host, port);
                HostPeer = new Peer { T = udp, Tid = 0, Id = 0, Name = host, LastHeard = Time.unscaledTime };
                Status = $"CONNECTING TO {host}:{port}...";
                Plugin.Log.LogInfo($"Online: dialling {host}:{port}");
                SayHello();
            }
            catch (Exception e) { Fail("COULDN'T REACH " + address + " (" + e.Message + ")"); }
        }

        public static void JoinSteam(CSteamID hostId)
        {
            Begin(NetRole.Client);
            steam = SteamTransport.Dial(hostId);
            HostPeer = new Peer { T = steam, Tid = 0, Id = 0, Name = SteamTransport.NameOf(hostId), LastHeard = Time.unscaledTime };
            Status = "CONNECTING TO " + HostPeer.Name.ToUpperInvariant() + "...";
            Plugin.Log.LogInfo("Online: joining " + HostPeer.Name + " over Steam");
            SayHello();
        }

        private static void SayHello()
        {
            lastHello = Time.unscaledTime;
            Send(new NetWriter(Msg.Hello).I(Protocol).S(Plugin.Version).S(MyName));
        }

        // ends the session; tell = let the others know
        public static void Stop(string reason, bool tell = true)
        {
            if (Role == NetRole.None && udp == null && steam == null) return;
            if (tell) { try { Send(new NetWriter(Msg.Bye).S(reason ?? "")); Flush(); } catch { } }
            udp?.Close(); steam?.Close();
            udp = null; steam = null;
            Lobbies.Leave();
            bool was = Role != NetRole.None;
            if (was) Application.runInBackground = savedRunInBackground;
            Role = NetRole.None;
            Peers.Clear();
            HostPeer = null;
            Roster.Clear();
            Status = "";
            RemotePads.Clear();
            ClientPads.Clear();
            inbox.Clear();
            if (was)
            {
                Plugin.Log.LogInfo("Online: session ended" + (reason != null ? " (" + reason + ")" : ""));
                OnClosed?.Invoke(reason);
            }
        }

        // the last reliable messages (a goodbye) go out before the sockets close
        private static void Flush()
        {
            var sink = new List<(int, byte[])>();
            udp?.Poll(sink); steam?.Poll(sink);
        }

        private static void Fail(string reason)
        {
            Plugin.Log.LogWarning("Online: " + reason);
            Stop(reason, false);
        }

        // host: a friend leaves the session (said bye, timed out, or the lobby closed for them)
        private static void DropPeer(Peer p, string why)
        {
            if (!Peers.Remove(p)) return;
            p.T.Drop(p.Tid);
            foreach (var k in RemotePads.Keys.Where(k => k / 10000 == p.Id).ToList()) RemotePads.Remove(k);
            if (!p.Welcomed) return;
            Plugin.Log.LogInfo($"Online: {p.Name} left ({why})");
            OnlineFlow.SystemChat($"{p.Name.ToUpperInvariant()} {why}");
            RefreshRoster();
            OnPeerLeft?.Invoke(p);
        }

        // ------------------------------------------------------------ the frame loop (NetRunner)

        internal static void EarlyUpdate()
        {
            if (Role == NetRole.None) return;
            float now = Time.unscaledTime;
            Receive(udp, now);
            if (Role == NetRole.None) return;
            Receive(steam, now);
            if (Role == NetRole.None) return;
            foreach (var p in RemotePads.Values) p.Step();

            if (IsClient)
            {
                if (!HostPeer.Welcomed && now - lastHello > 1f) SayHello();
                if (!HostPeer.Welcomed && now - startedAt > 12f) { Fail("NO ANSWER FROM THE HOST"); return; }
                if (HostPeer.Welcomed && now - HostPeer.LastHeard > Plugin.OnlineTimeout.Value) { Fail("THE HOST STOPPED RESPONDING"); return; }
            }
            else
            {
                foreach (var p in Peers.ToList())
                {
                    if (p.Welcomed && now - p.LastHeard > Plugin.OnlineTimeout.Value) DropPeer(p, "STOPPED RESPONDING");
                    else if (!p.Welcomed && now - p.LastHeard > 15f) DropPeer(p, "");
                }
                if (now - lastRoster > 1f) { lastRoster = now; RefreshRoster(); }
            }
            if (now - lastPing > 0.5f) { lastPing = now; Send(new NetWriter(Msg.Ping).F(now), false); }
        }

        private static void Receive(ITransport t, float now)
        {
            if (t == null) return;
            inbox.Clear();
            t.Poll(inbox);
            foreach (var (tid, d) in inbox.ToList())
            {
                NetReader r;
                try { r = new NetReader(d); } catch { continue; }
                Peer p;
                if (IsClient) { p = HostPeer; if (p == null || p.T != t) continue; }
                else
                {
                    p = Peers.FirstOrDefault(x => x.T == t && x.Tid == tid);
                    if (p == null) { p = new Peer { T = t, Tid = tid }; Peers.Add(p); }
                }
                p.LastHeard = now;
                try { Handle(r, p); }
                catch (Exception e) { Plugin.Log.LogError($"Online: message {r.Id} failed: {e}"); }
                if (Role == NetRole.None) return;                       // a handler ended the session
            }
        }

        internal static void LateUpdate()
        {
            if (IsClient && Connected) SendPads();
        }

        private static void Handle(NetReader r, Peer from)
        {
            switch (r.Id)
            {
                case Msg.Hello when IsHost:
                {
                    int proto = r.I(); string ver = r.S(); string name = r.S();
                    bool watch = false;
                    string refuse = null;
                    if (proto != Protocol || ver != Plugin.Version)
                        refuse = ver == Plugin.Version ? $"DIFFERENT OVERTIME {ver} BUILDS: EVERYONE NEEDS THE NEWEST ZIP"
                                                       : $"VERSION MISMATCH: THE HOST HAS OVERTIME {Plugin.Version}, YOU HAVE {ver}";
                    else if (!from.Welcomed && Peers.Count(p => p.Welcomed) >= Lobby.MaxPCs - 1) refuse = "THE LOBBY IS FULL";
                    else if (!from.Welcomed && OnlineFlow.MatchRunning) watch = true;                      // a match is on: they watch it
                    if (refuse != null)
                    {
                        SendTo(from, new NetWriter(Msg.Reject).S(refuse));
                        Plugin.Log.LogInfo($"Online: refused {name} ({refuse})");
                        return;
                    }
                    bool fresh = !from.Welcomed;
                    if (fresh)
                    {
                        from.Welcomed = true;
                        from.Name = name;
                        from.Id = Enumerable.Range(1, 3).First(i => Peers.All(p => p == from || p.Id != i));
                    }
                    SendTo(from, new NetWriter(Msg.Welcome).S(MyName).U8(from.Id).S(Lobby.Name).S(Lobby.Code).B(Lobby.Public).U8(Lobby.MaxPCs));
                    if (fresh && watch) { from.Spectator = true; SendTo(from, new NetWriter(Msg.Spectate).B(true)); }
                    if (fresh)
                    {
                        Plugin.Log.LogInfo($"Online: {name} joined as PC {from.Id + 1} ({from.T.Kind})");
                        OnlineFlow.SystemChat($"{name.ToUpperInvariant()} JOINED");
                        RefreshRoster();
                        OnPeerJoined?.Invoke(from);
                    }
                    return;
                }
                case Msg.Welcome when IsClient:
                {
                    if (from.Welcomed) return;
                    from.Name = r.S();
                    MyId = r.U8();
                    Lobby.Name = r.S(); Lobby.Code = r.S(); Lobby.Public = r.B(); Lobby.MaxPCs = r.U8();
                    from.Welcomed = true;
                    Status = "CONNECTED TO " + from.Name.ToUpperInvariant();
                    Plugin.Log.LogInfo($"Online: joined {from.Name}'s lobby '{Lobby.Name}' as PC {MyId + 1}");
                    break;
                }
                case Msg.Reject when IsClient:
                    Fail(r.S());
                    return;
                case Msg.Bye:
                {
                    string why = r.S();
                    if (IsHost) DropPeer(from, "LEFT");
                    else Stop(string.IsNullOrEmpty(why) ? "THE HOST LEFT" : why, false);
                    return;
                }
                case Msg.Ping:
                    SendTo(from, new NetWriter(Msg.Pong).F(r.F()), false);
                    return;
                case Msg.Pong:
                {
                    float ms = (Time.unscaledTime - r.F()) * 1000f;
                    from.PingMs = from.PingMs <= 0f ? ms : Mathf.Lerp(from.PingMs, ms, 0.3f);
                    return;
                }
                case Msg.Pads when IsHost && from.Welcomed:
                    ReadPads(r, from);
                    return;
                case Msg.Roster when IsClient:
                {
                    Roster.Clear();
                    int n = r.U8();
                    for (int i = 0; i < n; i++) { int id = r.U8(); string name = r.S(); int ping = r.I(); Roster[id] = (name, ping); }
                    if (Roster.TryGetValue(MyId, out var me)) Roster[MyId] = (me.name, Mathf.RoundToInt(PingMs));
                    return;
                }
            }
            if (!from.Welcomed && IsHost) return;                                 // nothing else before the handshake
            if (handlers.TryGetValue(r.Id, out var handler)) handler(r, from);
        }

        // host: who is here, with their pings, for everyone's players list
        private static void RefreshRoster()
        {
            if (!IsHost) return;
            Roster.Clear();
            Roster[0] = (MyName, 0);
            foreach (var p in Peers.Where(p => p.Welcomed)) Roster[p.Id] = (p.Name + (p.Spectator ? " (WATCHING)" : ""), Mathf.RoundToInt(p.PingMs));
            var w = new NetWriter(Msg.Roster).U8(Roster.Count);
            foreach (var kv in Roster) w.U8(kv.Key).S(kv.Value.name).I(kv.Value.ping);
            Send(w);
            Lobbies.UpdateMembers(Roster.Count);
        }

        public static Peer PeerById(int id) => Peers.FirstOrDefault(p => p.Welcomed && p.Id == id);
        public static string NameOfPc(int id) => Roster.TryGetValue(id, out var r) ? r.name : "PC " + (id + 1);

        // ------------------------------------------------------------ the friends' devices, read on the host

        // Presses travel as running counts, so a lost packet only delays a press, it never drops it.
        internal class RemotePad
        {
            public string Label;
            public float X;
            public readonly int[] Count = new int[6], Cur = new int[6], Prev = new int[6];
            public void Step() { Array.Copy(Cur, Prev, 6); Array.Copy(Count, Cur, 6); }
            public bool Edge(int k) => Cur[k] != Prev[k];
        }

        // keyed by PC id * 10000 + the device's key on that PC
        public static readonly Dictionary<int, RemotePad> RemotePads = new Dictionary<int, RemotePad>();
        public static int PcOf(int remoteKey) => remoteKey / 10000;
        public static int DeviceOf(int remoteKey) => remoteKey % 10000;

        private static void ReadPads(NetReader r, Peer from)
        {
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                int key = from.Id * 10000 + r.I(); string label = r.S(); float x = r.F();
                if (!RemotePads.TryGetValue(key, out var p)) RemotePads[key] = p = new RemotePad();
                p.Label = label; p.X = x;
                // each PC counts each device's presses from 0, so the first packet's counts are all new presses
                for (int k = 0; k < 6; k++) { int c = r.I(); if (c > p.Count[k]) p.Count[k] = c; }
            }
        }

        public static PadState ReadRemote(int key)
        {
            var s = new PadState();
            if (!IsHost || PeerById(PcOf(key)) == null || !RemotePads.TryGetValue(key, out var p)) return s;
            s.Connected = true;
            s.X = p.X;
            s.Up = p.Edge(0); s.Down = p.Edge(1); s.Left = p.Edge(2); s.Right = p.Edge(3); s.Confirm = p.Edge(4); s.Back = p.Edge(5);
            return s;
        }

        // a friend's device pressed something this frame and isn't in a seat yet
        public static SlotInput RemoteJoin(IEnumerable<SlotInput> taken)
        {
            if (!IsHost) return null;
            foreach (var kv in RemotePads)
            {
                var p = kv.Value;
                if (!(p.Edge(0) || p.Edge(1) || p.Edge(2) || p.Edge(3) || p.Edge(4))) continue;
                if (taken.Any(t => t != null && t.Kind == InputKind.Remote && t.DeviceId == kv.Key)) continue;
                var pc = PeerById(PcOf(kv.Key));
                if (pc == null || pc.Spectator) continue;                                // a spectator's devices take no seat
                return new SlotInput { Kind = InputKind.Remote, DeviceId = kv.Key, Label = $"{p.Label} ({pc.Name.ToUpperInvariant()})" };
            }
            return null;
        }

        // ------------------------------------------------------------ a friend's side: every local device, sent each frame

        private class ClientPad
        {
            public SlotInput Input;
            public readonly int[] Count = new int[6];
            public float X;
            public bool Active;
        }

        private static readonly Dictionary<int, ClientPad> ClientPads = new Dictionary<int, ClientPad>();
        private static readonly Dictionary<int, PadState> overrides = new Dictionary<int, PadState>();

        public static int KeyOf(SlotInput i) => i == null ? 0 : i.Kind == InputKind.Gamepad ? 1000 + i.DeviceId : (int)i.Kind;

        // a remote slot that belongs to this PC (its device key here), or -1
        public static int MyDevice(SlotInput i) => IsClient && i?.Kind == InputKind.Remote && PcOf(i.DeviceId) == MyId ? DeviceOf(i.DeviceId) : -1;

        // the autopilot (development) stands in for a device this frame
        public static void OverridePad(int key, PadState s) => overrides[key] = s;

        private static void SendPads()
        {
            var devices = VersusInput.KeyboardSets.Select(k => new SlotInput { Kind = k.kind, Label = k.label })
                .Concat(Gamepad.all.Select((g, i) => new SlotInput { Kind = InputKind.Gamepad, DeviceId = g.deviceId, Label = "GAMEPAD " + (i + 1) }));
            foreach (var d in devices)
            {
                int key = KeyOf(d);
                if (!ClientPads.TryGetValue(key, out var p)) ClientPads[key] = p = new ClientPad { Input = d };
                var s = overrides.TryGetValue(key, out var o) ? o : VersusInput.Read(d);
                if (Patches.ChatHud.DeviceBusy(key)) s = new PadState { Connected = true };   // its player is chatting
                p.X = s.X;
                if (s.Up) p.Count[0]++;
                if (s.Down) p.Count[1]++;
                if (s.Left) p.Count[2]++;
                if (s.Right) p.Count[3]++;
                if (s.Confirm) p.Count[4]++;
                if (s.Back) p.Count[5]++;
                if (s.Up || s.Down || s.Left || s.Right || s.Confirm || overrides.ContainsKey(key)) p.Active = true;
            }
            overrides.Clear();
            var active = ClientPads.Where(kv => kv.Value.Active).Take(8).ToList();
            if (active.Count == 0) return;
            var w = new NetWriter(Msg.Pads).U8(active.Count);
            foreach (var kv in active)
            {
                w.I(kv.Key).S(kv.Value.Input.Label).F(kv.Value.X);
                for (int k = 0; k < 6; k++) w.I(kv.Value.Count[k]);
            }
            Send(w, false);
        }
    }

    [DefaultExecutionOrder(-500)]
    internal class NetRunner : MonoBehaviour
    {
        private void Update()
        {
            Lobbies.Hook();
            Net.EarlyUpdate();
            Patches.ChatHud.Tick();
            SpectatorHud.Tick();
            OnlineFlow.TitleTick();
            PingHud.Tick();
            TitleBar.Tick();
        }

        private void LateUpdate() => Net.LateUpdate();
        private void OnApplicationQuit() => Net.Stop(Net.IsHost ? "THE HOST CLOSED THE GAME" : null);
    }
}

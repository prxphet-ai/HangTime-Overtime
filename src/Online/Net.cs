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

    // The online session: one host (runs the real match) and one client (mirrors it and sends its players' inputs).
    // Hosting listens on Steam (friend invites) and on direct IP at once; the first friend to say hello is the peer.
    internal static class Net
    {
        public const int Protocol = 2;               // 2: perk effects travel to the client

        public static NetRole Role { get; private set; }
        public static bool Connected { get; private set; }           // handshake done
        public static bool IsHost => Role == NetRole.Host;
        public static bool IsClient => Role == NetRole.Client;
        public static bool Online => Role != NetRole.None;
        public static string PeerName = "";
        public static string Status = "";                              // one line for the menus
        public static float PingMs;
        public static event Action<string> OnClosed;                   // reason

        private static INetLink link;                                  // the peer's link once known
        private static UdpLink udp;                                    // host: listening; client: dialled
        private static SteamLink steam;
        private static float lastHeard, lastPing, lastHello, startedAt;
        private static readonly Dictionary<Msg, Action<NetReader>> handlers = new Dictionary<Msg, Action<NetReader>>();
        private static readonly List<byte[]> inbox = new List<byte[]>();

        public static void On(Msg id, Action<NetReader> h) => handlers[id] = h;

        public static void Send(NetWriter w, bool reliable = true)
        {
            if (link == null) return;
            link.Send(w.Bytes(), reliable);
        }

        private static string MyName()
        {
            try { if (SteamLink.Available) return SteamFriends.GetPersonaName(); } catch { }
            return Environment.UserName;
        }

        // ------------------------------------------------------------ starting and stopping

        // an online game keeps running while its window is in the background (alt-tab must not freeze the other player)
        private static bool savedRunInBackground;
        private static void KeepRunning()
        {
            savedRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
        }

        public static void StartHost()
        {
            Stop(null, false);
            KeepRunning();
            Role = NetRole.Host;
            startedAt = Time.unscaledTime;
            var parts = new List<string>();
            try
            {
                udp = UdpLink.Listen(Plugin.OnlineBind.Value, Plugin.OnlinePort.Value);
                parts.Add($"IP PORT {Plugin.OnlinePort.Value}");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Online: direct IP unavailable: " + e.Message); }
            if (SteamLink.Available)
            {
                steam = SteamLink.Host();
                SteamCalls.CreateLobby(steam);
                parts.Add("STEAM");
            }
            Status = parts.Count == 0 ? "COULDN'T START HOSTING" : "WAITING FOR A FRIEND (" + string.Join(" · ", parts) + ")";
            Plugin.Log.LogInfo("Online: hosting via " + string.Join(", ", parts));
        }

        public static void JoinIp(string address)
        {
            Stop(null, false);
            KeepRunning();
            Role = NetRole.Client;
            startedAt = Time.unscaledTime;
            try
            {
                string host = address; int port = Plugin.OnlinePort.Value;
                int colon = address.LastIndexOf(':');
                if (colon > 0 && int.TryParse(address.Substring(colon + 1), out var p)) { host = address.Substring(0, colon); port = p; }
                udp = UdpLink.Dial(host, port);
                link = udp;
                Status = $"CONNECTING TO {host}:{port}...";
                Plugin.Log.LogInfo($"Online: dialling {host}:{port}");
                SayHello();
            }
            catch (Exception e) { Fail("COULDN'T REACH " + address + " (" + e.Message + ")"); }
        }

        public static void JoinSteam(CSteamID hostId)
        {
            Stop(null, false);
            KeepRunning();
            Role = NetRole.Client;
            startedAt = Time.unscaledTime;
            steam = SteamLink.Dial(hostId);
            link = steam;
            Status = "CONNECTING TO " + steam.PeerName.ToUpperInvariant() + "...";
            Plugin.Log.LogInfo("Online: joining " + steam.PeerName + " over Steam");
            SayHello();
        }

        private static void SayHello()
        {
            lastHello = Time.unscaledTime;
            Send(new NetWriter(Msg.Hello).I(Protocol).S(Plugin.Version).S(MyName()));
        }

        // ends the session; tell = let the other side know
        public static void Stop(string reason, bool tell = true)
        {
            if (Role == NetRole.None && link == null && udp == null && steam == null) return;
            if (tell && link != null) { try { link.Send(new NetWriter(Msg.Bye).S(reason ?? "").Bytes(), true); link.Poll(inbox); } catch { } }
            udp?.Close(); steam?.Close();
            udp = null; steam = null; link = null;
            bool was = Role != NetRole.None;
            if (was) Application.runInBackground = savedRunInBackground;
            Role = NetRole.None;
            Connected = false;
            PeerName = "";
            Status = "";
            PingMs = 0f;
            RemotePads.Clear();
            ClientPads.Clear();
            inbox.Clear();
            if (was)
            {
                Plugin.Log.LogInfo("Online: session ended" + (reason != null ? " (" + reason + ")" : ""));
                OnClosed?.Invoke(reason);
            }
        }

        private static void Fail(string reason)
        {
            Plugin.Log.LogWarning("Online: " + reason);
            Stop(reason, false);
        }

        // ------------------------------------------------------------ the frame loop (NetRunner)

        internal static void EarlyUpdate()
        {
            if (Role == NetRole.None) return;
            float now = Time.unscaledTime;
            inbox.Clear();
            // the host hears from whichever link a friend uses first, then sticks to it
            if (udp != null && (link == null || link == udp)) { udp.Poll(inbox); if (link == null && inbox.Count > 0) { link = udp; steam?.Close(); steam = null; } }
            if (steam != null && (link == null || link == steam)) { int before = inbox.Count; steam.Poll(inbox); if (link == null && inbox.Count > before) { link = steam; udp?.Close(); udp = null; } }
            foreach (var d in inbox.ToList())
            {
                NetReader r;
                try { r = new NetReader(d); } catch { continue; }
                lastHeard = now;
                try { Handle(r); }
                catch (Exception e) { Plugin.Log.LogError($"Online: message {r.Id} failed: {e}"); }
                if (Role == NetRole.None) return;                       // a handler ended the session
            }
            foreach (var p in RemotePads.Values) p.Step();

            if (IsHost && link == null && now - lastPing > 5f)
            {
                lastPing = now;
                Plugin.Log.LogInfo($"Online: hosting, waiting (direct IP {(udp != null ? "listening" : "off")}, Steam {(steam != null ? "on" : "off")})");
            }
            if (link == null) return;
            if (!Connected && IsClient && now - lastHello > 1f) SayHello();
            if (!Connected && IsClient && now - startedAt > 12f) { Fail("NO ANSWER FROM THE HOST"); return; }
            if (now - lastPing > 0.5f) { lastPing = now; Send(new NetWriter(Msg.Ping).F(now), false); }
            if (Connected && now - lastHeard > Plugin.OnlineTimeout.Value) Fail((IsHost ? PeerName : "THE HOST") + " STOPPED RESPONDING");
        }

        internal static void LateUpdate()
        {
            if (IsClient && Connected) SendPads();
        }

        private static void Handle(NetReader r)
        {
            switch (r.Id)
            {
                case Msg.Hello when IsHost:
                {
                    int proto = r.I(); string ver = r.S(); string name = r.S();
                    if (proto != Protocol || ver != Plugin.Version)
                    {
                        string why = ver == Plugin.Version
                            ? $"DIFFERENT OVERTIME {ver} BUILDS: BOTH PLAYERS NEED THE NEWEST ZIP"
                            : $"VERSION MISMATCH: HOST HAS OVERTIME {Plugin.Version}, YOU HAVE {ver}";
                        link.Send(new NetWriter(Msg.Reject).S(why).Bytes(), true);
                        Plugin.Log.LogInfo($"Online: rejected {name} (version {ver}, protocol {proto})");
                        return;
                    }
                    bool fresh = !Connected;
                    Connected = true;
                    PeerName = name;
                    Status = name.ToUpperInvariant() + " CONNECTED";
                    Send(new NetWriter(Msg.Welcome).S(MyName()));
                    if (fresh) { Plugin.Log.LogInfo($"Online: {name} connected ({(link == udp ? "direct IP" : "Steam")})"); handlers.TryGetValue(Msg.Hello, out var h); h?.Invoke(r); }
                    return;
                }
                case Msg.Welcome when IsClient:
                    if (Connected) return;
                    Connected = true;
                    PeerName = r.S();
                    Status = "CONNECTED TO " + PeerName.ToUpperInvariant();
                    Plugin.Log.LogInfo("Online: connected to " + PeerName);
                    break;
                case Msg.Reject when IsClient:
                    Fail(r.S());
                    return;
                case Msg.Bye:
                {
                    string why = r.S();
                    Stop(string.IsNullOrEmpty(why) ? (IsHost ? PeerName + " LEFT" : "THE HOST LEFT") : why, false);
                    return;
                }
                case Msg.Ping:
                    Send(new NetWriter(Msg.Pong).F(r.F()), false);
                    return;
                case Msg.Pong:
                {
                    float ms = (Time.unscaledTime - r.F()) * 1000f;
                    PingMs = PingMs <= 0f ? ms : Mathf.Lerp(PingMs, ms, 0.3f);
                }
                    return;
                case Msg.Pads when IsHost:
                    ReadPads(r);
                    return;
            }
            if (handlers.TryGetValue(r.Id, out var handler)) handler(r);
        }

        // ------------------------------------------------------------ the client's devices, read on the host

        // Presses travel as running counts, so a lost packet only delays a press, it never drops it.
        internal class RemotePad
        {
            public string Label;
            public float X;
            public readonly int[] Count = new int[6], Cur = new int[6], Prev = new int[6];
            public bool Seen;
            public void Step() { Array.Copy(Cur, Prev, 6); Array.Copy(Count, Cur, 6); }
            public bool Edge(int k) => Cur[k] != Prev[k];
        }

        public static readonly Dictionary<int, RemotePad> RemotePads = new Dictionary<int, RemotePad>();

        private static void ReadPads(NetReader r)
        {
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                int key = r.I(); string label = r.S(); float x = r.F();
                if (!RemotePads.TryGetValue(key, out var p))
                {
                    p = new RemotePad();
                    RemotePads[key] = p;
                }
                p.Label = label; p.X = x;
                // the client counts each device's presses from 0, so the first packet's counts are all new presses
                for (int k = 0; k < 6; k++) { int c = r.I(); if (c > p.Count[k]) p.Count[k] = c; }
            }
        }

        public static PadState ReadRemote(int key)
        {
            var s = new PadState();
            if (!IsHost || !Connected || !RemotePads.TryGetValue(key, out var p)) return s;
            s.Connected = true;
            s.X = p.X;
            s.Up = p.Edge(0); s.Down = p.Edge(1); s.Left = p.Edge(2); s.Right = p.Edge(3); s.Confirm = p.Edge(4); s.Back = p.Edge(5);
            return s;
        }

        // a client device pressed something this frame and isn't in a seat yet
        public static SlotInput RemoteJoin(IEnumerable<SlotInput> taken)
        {
            if (!IsHost || !Connected) return null;
            foreach (var kv in RemotePads)
            {
                var p = kv.Value;
                if (!(p.Edge(0) || p.Edge(1) || p.Edge(2) || p.Edge(3) || p.Edge(4))) continue;
                if (taken.Any(t => t != null && t.Kind == InputKind.Remote && t.DeviceId == kv.Key)) continue;
                return new SlotInput { Kind = InputKind.Remote, DeviceId = kv.Key, Label = $"{p.Label} ({PeerName.ToUpperInvariant()})" };
            }
            return null;
        }

        // ------------------------------------------------------------ the client's side: every local device, sent each frame

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

        // the client's autopilot (development) stands in for a device this frame
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

    // Steam lobby calls: creating, inviting, and joining from an invite or the friends list.
    internal static class SteamCalls
    {
        private static CallResult<LobbyCreated_t> created;
        private static CallResult<LobbyEnter_t> entered;
        private static Callback<GameLobbyJoinRequested_t> joinRequested;
        private static SteamLink hosting;
        private static bool hooked;
        public static CSteamID PendingLobby;          // from the command line (+connect_lobby) or a join request before the menu was ready

        public static void Hook()
        {
            if (hooked || !SteamLink.Available) return;
            hooked = true;
            created = CallResult<LobbyCreated_t>.Create(OnCreated);
            entered = CallResult<LobbyEnter_t>.Create(OnEntered);
            joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => { PendingLobby = r.m_steamIDLobby; Plugin.Log.LogInfo("Online: Steam join request"); });
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "+connect_lobby");
            if (i >= 0 && i + 1 < args.Length && ulong.TryParse(args[i + 1], out var id)) PendingLobby = new CSteamID(id);
        }

        public static void CreateLobby(SteamLink link)
        {
            Hook();
            hosting = link;
            created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 2));
        }

        private static void OnCreated(LobbyCreated_t r, bool failed)
        {
            if (failed || r.m_eResult != EResult.k_EResultOK || hosting == null) { Plugin.Log.LogWarning("Online: Steam lobby failed " + r.m_eResult); return; }
            hosting.Lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(hosting.Lobby, "overtime", Plugin.Version);
            Plugin.Log.LogInfo("Online: Steam lobby ready");
        }

        public static bool CanInvite => hosting != null && hosting.Lobby.IsValid() && Net.IsHost;
        public static void Invite() { if (CanInvite) SteamFriends.ActivateGameOverlayInviteDialog(hosting.Lobby); }

        // the menu takes a pending join when it is ready for it
        public static void JoinPending()
        {
            if (!PendingLobby.IsValid()) return;
            var lobby = PendingLobby;
            PendingLobby = CSteamID.Nil;
            entered.Set(SteamMatchmaking.JoinLobby(lobby));
        }

        private static void OnEntered(LobbyEnter_t r, bool failed)
        {
            var lobby = new CSteamID(r.m_ulSteamIDLobby);
            if (failed || r.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess) { Plugin.Log.LogWarning("Online: couldn't enter the lobby"); return; }
            string ver = SteamMatchmaking.GetLobbyData(lobby, "overtime");
            var owner = SteamMatchmaking.GetLobbyOwner(lobby);
            if (owner == SteamUser.GetSteamID()) return;                  // our own lobby
            Net.JoinSteam(owner);
            Plugin.Log.LogInfo($"Online: entered lobby (host version {ver})");
        }
    }

    [DefaultExecutionOrder(-500)]
    internal class NetRunner : MonoBehaviour
    {
        private void Update()
        {
            SteamCalls.Hook();
            Net.EarlyUpdate();
            VersusMenu.TitleTick();
            PingHud.Tick();
        }

        private void LateUpdate() => Net.LateUpdate();
        private void OnApplicationQuit() => Net.Stop("THE OTHER PLAYER CLOSED THE GAME");
    }
}

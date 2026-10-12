using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace HangtimeOvertime.Online
{
    internal class LobbyEntry
    {
        public CSteamID Id;
        public string Name = "", Version = "", Mode = "", State = "", Code = "";
        public int Pcs, Max;
        public bool SameVersion => Version == Lobbies.VersionKey;
        public bool Full => Pcs >= Max;
    }

    // Steam lobbies: the directory people browse. A lobby is created when someone hosts and carries the lobby's name,
    // code, mode, state and PC count as lobby data; joining one connects to its owner (the host). Private lobbies are
    // "invisible" Steam lobbies: left out of the browser list, but a code search still finds them.
    internal static class Lobbies
    {
        public static string VersionKey => Plugin.Version + "/" + Net.Protocol;
        private const string Marker = "hangtime_overtime";

        private static CallResult<LobbyCreated_t> created;
        private static CallResult<LobbyEnter_t> entered;
        private static CallResult<LobbyMatchList_t> listed;
        private static Callback<GameLobbyJoinRequested_t> joinRequested;
        private static bool hooked;
        private static CSteamID current;               // the lobby this PC is in (hosting or joined)
        private static Action<List<LobbyEntry>> onList;
        public static CSteamID PendingLobby;            // an invite / "Join game" / +connect_lobby, taken when the menus are ready
        public static string LastError = "";

        public static bool Available => SteamTransport.Available;
        public static bool InLobby => current.IsValid();

        public static void Hook()
        {
            if (hooked || !Available) return;
            hooked = true;
            created = CallResult<LobbyCreated_t>.Create(OnCreated);
            entered = CallResult<LobbyEnter_t>.Create(OnEntered);
            listed = CallResult<LobbyMatchList_t>.Create(OnListed);
            joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => { PendingLobby = r.m_steamIDLobby; Plugin.Log.LogInfo("Online: Steam join request"); });
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "+connect_lobby");
            if (i >= 0 && i + 1 < args.Length && ulong.TryParse(args[i + 1], out var id)) PendingLobby = new CSteamID(id);
        }

        public static string NewCode()
        {
            const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no 0/O, 1/I
            var c = new char[6];
            for (int i = 0; i < c.Length; i++) c[i] = letters[UnityEngine.Random.Range(0, letters.Length)];
            return new string(c);
        }

        // ------------------------------------------------------------ hosting

        public static void Create(LobbyOptions o)
        {
            Hook();
            if (!Available) return;
            created.Set(SteamMatchmaking.CreateLobby(o.Public ? ELobbyType.k_ELobbyTypePublic : ELobbyType.k_ELobbyTypeInvisible, o.MaxPCs));
        }

        private static void OnCreated(LobbyCreated_t r, bool failed)
        {
            if (failed || r.m_eResult != EResult.k_EResultOK) { Plugin.Log.LogWarning("Online: Steam lobby failed " + r.m_eResult); return; }
            if (!Net.IsHost) { SteamMatchmaking.LeaveLobby(new CSteamID(r.m_ulSteamIDLobby)); return; }   // stopped hosting meanwhile
            current = new CSteamID(r.m_ulSteamIDLobby);
            Set(Marker, "1");
            Set("ver", VersionKey);
            Set("name", Net.Lobby.Name);
            Set("code", Net.Lobby.Code);
            Set("hidden", Net.Lobby.Public ? "0" : "1");
            Set("max", Net.Lobby.MaxPCs.ToString());
            Set("pcs", Mathf.Max(1, Net.Roster.Count).ToString());
            Set("state", "LOBBY");
            Set("mode", "1v1");
            Plugin.Log.LogInfo($"Online: Steam lobby ready ({(Net.Lobby.Public ? "public" : "private")})");
        }

        private static void Set(string k, string v) { if (current.IsValid() && Net.IsHost) SteamMatchmaking.SetLobbyData(current, k, v); }

        public static void UpdateMembers(int pcs) => Set("pcs", pcs.ToString());
        public static void SetMode(string format) => Set("mode", format);

        public static void SetInMatch(bool inMatch)
        {
            if (!current.IsValid() || !Net.IsHost) return;
            Set("state", inMatch ? "IN MATCH" : "LOBBY");
            SteamMatchmaking.SetLobbyJoinable(current, !inMatch);
        }

        public static bool CanInvite => Available && current.IsValid() && Net.Online;
        public static void Invite() { if (CanInvite) SteamFriends.ActivateGameOverlayInviteDialog(current); }

        public static void Leave()
        {
            if (current.IsValid() && Available) SteamMatchmaking.LeaveLobby(current);
            current = CSteamID.Nil;
        }

        // ------------------------------------------------------------ browsing

        // every Overtime lobby worldwide (Florida sees Canada); code: only that lobby, private ones included
        public static bool RequestList(bool sameVersion, bool showFull, string code, Action<List<LobbyEntry>> done)
        {
            Hook();
            if (!Available) { LastError = "STEAM ISN'T RUNNING"; return false; }
            onList = done;
            SteamMatchmaking.AddRequestLobbyListStringFilter(Marker, "1", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            if (!string.IsNullOrEmpty(code)) SteamMatchmaking.AddRequestLobbyListStringFilter("code", code.Trim().ToUpperInvariant(), ELobbyComparison.k_ELobbyComparisonEqual);
            else SteamMatchmaking.AddRequestLobbyListStringFilter("hidden", "0", ELobbyComparison.k_ELobbyComparisonEqual);
            if (sameVersion) SteamMatchmaking.AddRequestLobbyListStringFilter("ver", VersionKey, ELobbyComparison.k_ELobbyComparisonEqual);
            if (!showFull) SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(50);
            listed.Set(SteamMatchmaking.RequestLobbyList());
            return true;
        }

        private static void OnListed(LobbyMatchList_t r, bool failed)
        {
            var list = new List<LobbyEntry>();
            if (!failed)
                for (int i = 0; i < r.m_nLobbiesMatching; i++)
                {
                    var id = SteamMatchmaking.GetLobbyByIndex(i);
                    var e = new LobbyEntry
                    {
                        Id = id,
                        Name = SteamMatchmaking.GetLobbyData(id, "name"),
                        Version = SteamMatchmaking.GetLobbyData(id, "ver"),
                        Mode = SteamMatchmaking.GetLobbyData(id, "mode"),
                        State = SteamMatchmaking.GetLobbyData(id, "state"),
                        Code = SteamMatchmaking.GetLobbyData(id, "code"),
                    };
                    int.TryParse(SteamMatchmaking.GetLobbyData(id, "pcs"), out e.Pcs);
                    if (!int.TryParse(SteamMatchmaking.GetLobbyData(id, "max"), out e.Max)) e.Max = SteamMatchmaking.GetLobbyMemberLimit(id);
                    e.Pcs = Mathf.Max(e.Pcs, SteamMatchmaking.GetNumLobbyMembers(id));
                    if (string.IsNullOrEmpty(e.Name)) e.Name = "Lobby";
                    list.Add(e);
                }
            Plugin.Log.LogInfo($"Online: lobby list: {list.Count} found");
            var cb = onList; onList = null;
            cb?.Invoke(list);
        }

        // ------------------------------------------------------------ joining

        public static void Join(CSteamID lobby)
        {
            Hook();
            if (!Available) return;
            LastError = "";
            entered.Set(SteamMatchmaking.JoinLobby(lobby));
        }

        public static bool TakePending()
        {
            if (!PendingLobby.IsValid()) return false;
            var l = PendingLobby;
            PendingLobby = CSteamID.Nil;
            Join(l);
            return true;
        }

        private static void OnEntered(LobbyEnter_t r, bool failed)
        {
            var lobby = new CSteamID(r.m_ulSteamIDLobby);
            if (failed || r.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                LastError = r.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseFull ? "THAT LOBBY IS FULL" : "COULDN'T ENTER THAT LOBBY";
                Plugin.Log.LogWarning("Online: " + LastError);
                return;
            }
            var owner = SteamMatchmaking.GetLobbyOwner(lobby);
            if (owner == SteamUser.GetSteamID()) return;                  // our own lobby
            Net.JoinSteam(owner);
            current = lobby;                                               // after JoinSteam (it starts a fresh session)
            Plugin.Log.LogInfo($"Online: entered lobby '{SteamMatchmaking.GetLobbyData(lobby, "name")}' (host version {SteamMatchmaking.GetLobbyData(lobby, "ver")})");
        }
    }
}

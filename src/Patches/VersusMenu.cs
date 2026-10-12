using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HangtimeOvertime.Online;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // Title screen VERSUS: choose 1 VS 1, 1 VS 2 or 2 VS 2; each player presses a button on their own device to join (join
    // order fills the seats: left side first), each side's first player picks its team, everyone readies up, and the
    // match starts when every seat is filled and ready.
    // The same screen is the online lobby (MULTIPLAYER): the lobby's name and code, the PCs in it with their pings, chat,
    // INVITE / LEAVE; the host runs the seats and every friend's PC shows a copy (their keys travel to the host).
    internal class VersusMenu : OvertimePanel
    {
        private static readonly AccessTools.FieldRef<ButtonController, OnClick> onClickOf = AccessTools.FieldRefAccess<ButtonController, OnClick>("onClick");
        private static readonly AccessTools.FieldRef<TitleButton, TitleController> titleOf = AccessTools.FieldRefAccess<TitleButton, TitleController>("title");
        private static VersusMenu open;
        private const int MaxPlayers = 4;

        private TitleButton classic;
        private bool lobby;                                                   // the online lobby screen
        private ButtonGroup titleGroup;
        private readonly List<Collider2D> disabled = new List<Collider2D>();
        private int format;                                                   // index into VersusSetup.Formats
        private readonly SlotInput[] input = new SlotInput[MaxPlayers];       // by player number - 1 (join order)
        private readonly bool[] ready = new bool[MaxPlayers];
        private readonly int[] team = new int[2];
        private readonly List<string>[] teams = { new List<string>(), new List<string>() };
        private readonly TextMeshPro[,] rows = new TextMeshPro[2, 2];
        private readonly SpriteRenderer[,] rowBoxes = new SpriteRenderer[2, 2];
        private readonly TextMeshPro[] teamText = new TextMeshPro[2], sideTitle = new TextMeshPro[2];
        private readonly SpriteRenderer[] swatch = new SpriteRenderer[2], formatTabs = new SpriteRenderer[3];
        private TextMeshPro heading, status, info, playersText, chatText;
        private Field chatField;
        private float openedAt, nextPlayers, nextState;
        private bool starting;
        private static TitleButton classicRef;
        private static int lastLobbyFormat;
        private static string lastCode;                                   // the lobby keeps its mode between matches
        private string lastSent;
        private bool Mirror => Net.IsClient && Net.Connected;                 // a friend's PC shows the host's lobby

        public static bool IsOpen => open != null;
        public static TitleButton ClassicButton => classicRef;
        public static bool TitleReady => classicRef != null && classicRef.gameObject.activeInHierarchy;

        private string Format => VersusSetup.Formats[format];
        private (int side, string role)[] Seats => VersusSetup.Seats(Format);
        private int Count => Seats.Length;
        private int Leader(int side) => System.Array.FindIndex(Seats, x => x.side == side);   // seat that picks the side's team

        // ------------------------------------------------------------ the title buttons (VERSUS above CUSTOMIZE, MULTIPLAYER below)

        public static void AddTitleButton(GameObject classicButton)
        {
            if (classicButton == null) return;
            var group = classicButton.GetComponentInParent<ButtonGroup>(true);
            var customize = group?.buttons.FirstOrDefault(b => b != null && b.name == "Customize Button");
            if (group == null || customize == null || group.buttons.Any(b => b != null && b.name == "Versus Button")) return;
            var tb = classicButton.GetComponent<TitleButton>();
            classicRef = tb;
            var versus = Copy(customize, group, "Versus Button", "VERSUS", "1V1 · 1V2 · 2V2", () => Open(tb, false), 3.3f);
            group.buttons.Insert(group.buttons.IndexOf(customize), versus);
            var multi = Copy(customize, group, "Multiplayer Button", "ONLINE", "MULTIPLAYER", () => LobbyBrowser.Open(), -3.3f);
            group.buttons.Insert(group.buttons.IndexOf(customize) + 1, multi);
        }

        private static ButtonController Copy(ButtonController customize, ButtonGroup group, string name, string label, string sub, System.Action act, float dy)
        {
            var b = Instantiate(customize.gameObject, customize.transform.parent);
            b.name = name;
            foreach (var m in b.GetComponents<MonoBehaviour>().Where(m => m is PopupClick)) DestroyImmediate(m);
            var click = b.AddComponent<PopupClick>();
            click.Action = act;
            var ctrl = b.GetComponent<ButtonController>();
            onClickOf(ctrl) = click;
            ctrl.group = group;
            MenuPatches.SetButtonLabels(b, label, sub);
            b.transform.position = customize.transform.position + new Vector3(0f, dy, 0f);
            Traverse.Create(ctrl).Field("originalScale").SetValue(b.transform.localScale.x);
            return ctrl;
        }

        public static void OpenLobby() { if (classicRef != null) Open(classicRef, true); }

        private static void Open(TitleButton classicTb, bool lobby)
        {
            if (open != null) return;
            var go = new GameObject(lobby ? "Overtime Lobby" : "Overtime Versus Menu");
            open = go.AddComponent<VersusMenu>();
            open.classic = classicTb;
            open.lobby = lobby;
            if (!open.InitKit(200)) { Destroy(go); open = null; return; }
            open.Build();
        }

        // ------------------------------------------------------------ layout

        private void Build()
        {
            openedAt = Time.unscaledTime;
            titleGroup = classic.GetComponentInParent<ButtonGroup>(true);
            if (titleGroup != null)
            {
                titleGroup.enabled = false;
                foreach (var b in titleGroup.buttons.Where(b => b != null))
                    foreach (var c in b.GetComponents<Collider2D>().Where(c => c.enabled)) { c.enabled = false; disabled.Add(c); }
            }
            // left: your own team (Hoshiyumi, wearing your custom look) or a new team's colours; right: any team
            teams[0].Add("");
            teams[0].AddRange(Generated.Teams.All.Select(t => t.Id));
            teams[1].AddRange(Generated.Teams.VanillaRegular.Concat(Generated.Teams.VanillaCombo).Concat(Generated.Teams.VanillaBoss).Distinct());
            teams[1].AddRange(Generated.Teams.All.Select(t => t.Id));
            team[1] = Random.Range(0, teams[1].Count);
            if (lobby && Net.IsHost && Net.Lobby.Code == lastCode) format = lastLobbyFormat;   // back from a match: same mode
            if (lobby) lastCode = Net.Lobby.Code;

            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            float pw = W * (lobby ? 0.92f : 0.86f), ph = H * (lobby ? 0.88f : 0.84f);
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            // the seats: the whole panel, or its left part next to the lobby's players and chat
            float area = lobby ? pw * 0.62f : pw, centre = lobby ? -pw * 0.18f : 0f;
            float colW = area * (lobby ? 0.47f : 0.42f), colOff = area * (lobby ? 0.25f : 0.24f);
            heading = Text("VERSUS", new Vector2(centre, ph / 2f - 2f), lobby ? 1.1f : 1.4f, area - 2f, Color.white);
            if (lobby) info = Text("", new Vector2(centre, ph / 2f - 3.6f), 0.5f, area - 2f, new Color(1f, 0.85f, 0.35f));
            else Text($"ROUNDS TO {Generated.VersusRules.PointsPerRound} POINTS · FIRST TO {Generated.VersusRules.RoundsToWin} ROUNDS · THE LOSING SIDE PICKS AN UPGRADE EACH ROUND",
                      new Vector2(0f, ph / 2f - 3.8f), 0.45f, pw - 2f, new Color(0.85f, 0.95f, 1f));
            // mode tabs (mouse, or player 1's DOWN key)
            float tw = area * 0.2f;
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                formatTabs[i] = Button(VersusSetup.Formats[i].Replace("v", " VS "), new Vector2(centre + (i - 1) * tw * 1.1f, ph / 2f - 6f), new Vector2(tw, 2.2f), () => SetFormat(idx), 0.6f);
            }
            for (int side = 0; side < 2; side++)
            {
                float x = centre + (side == 0 ? -1f : 1f) * colOff;
                Box("Column", new Vector2(x, -1.6f), new Vector2(colW, ph * 0.56f), new Color(0.03f, 0.3f, 0.5f, 1f), 2);
                sideTitle[side] = Text(side == 0 ? "LEFT SIDE" : "RIGHT SIDE", new Vector2(x, ph * 0.17f), 0.8f, colW, Color.white);
                for (int r = 0; r < 2; r++)
                {
                    float y = ph * 0.06f - r * ph * 0.13f;
                    rowBoxes[side, r] = Box("Seat", new Vector2(x, y), new Vector2(colW * 0.9f, ph * 0.11f), new Color(0.05f, 0.38f, 0.6f, 1f), 3);
                    rows[side, r] = Text("", new Vector2(x, y), 0.55f, colW * 0.86f, Color.white, height: ph * 0.1f, wrap: true);
                }
                swatch[side] = Box("Team colour", new Vector2(x, -ph * 0.25f), new Vector2(colW * 0.72f, 2.6f), Color.white, 3);
                teamText[side] = Text("", new Vector2(x, -ph * 0.25f), 0.7f, colW * 0.72f, Color.white);
            }
            Text("JOIN: PRESS A KEY ON  W A S D,  ARROWS,  I J K L,  NUMPAD 8 4 5 6  OR  A ON A GAMEPAD   ·   LEFT / RIGHT: TEAM (EACH SIDE'S FIRST PLAYER)   ·   JUMP: READY   ·   DOWN (P1): MODE" +
                 (lobby ? "   ·   T: CHAT" : "   ·   ESC: BACK"),
                new Vector2(centre, -ph / 2f + 3.8f), 0.4f, area - 2f, new Color(0.85f, 0.95f, 1f), height: 2.4f, wrap: true);
            status = Text("", new Vector2(centre, -ph / 2f + 5.8f), 0.5f, area - 4f, new Color(1f, 0.85f, 0.35f));
            float by = -ph / 2f + 1.6f;
            if (!lobby)
            {
                Button("BACK", new Vector2(-pw * 0.12f, by), new Vector2(pw * 0.18f, 2.4f), () => Close(), 0.7f);
                Button("START", new Vector2(pw * 0.12f, by), new Vector2(pw * 0.18f, 2.4f), () => TryStart(), 0.7f);
            }
            else
            {
                Button("LEAVE", new Vector2(centre - area * 0.3f, by), new Vector2(area * 0.26f, 2.4f), () => LeaveLobby(), 0.7f);
                Button(Net.IsHost ? "START" : "HOST STARTS", new Vector2(centre + area * 0.02f, by), new Vector2(area * 0.3f, 2.4f), () => TryStart(), 0.7f);
                if (Lobbies.CanInvite || Lobbies.Available)
                    Button("INVITE (STEAM)", new Vector2(centre + area * 0.33f, by), new Vector2(area * 0.26f, 2.4f), () => Lobbies.Invite(), 0.6f);
                BuildSidebar(pw, ph);
            }
            Refresh();
        }

        // the lobby's PCs (with pings) and its chat
        private void BuildSidebar(float pw, float ph)
        {
            float x = pw * 0.31f, w = pw * 0.34f, top = ph / 2f;
            Box("Sidebar", new Vector2(x, -0.3f), new Vector2(w, ph - 2.2f), new Color(0.03f, 0.3f, 0.5f, 1f), 2);
            Text("PLAYERS", new Vector2(x, top - 2.2f), 0.7f, w - 1f, Color.white);
            playersText = Text("", new Vector2(x, top - 5.2f), 0.5f, w - 1.6f, Color.white, TextAlignmentOptions.Top, height: 5f, wrap: true);
            Text("CHAT", new Vector2(x, top - 9.4f), 0.7f, w - 1f, Color.white);
            chatText = Text("", new Vector2(x, -ph * 0.08f), 0.45f, w - 1.6f, Color.white, TextAlignmentOptions.Bottom, height: ph * 0.48f, wrap: true);
            chatField = TextField(new Vector2(x, -ph / 2f + 1.6f), new Vector2(w - 1.2f, 2.2f), "PRESS T OR CLICK TO CHAT", 120, t => { OnlineFlow.Say(t); SetField(chatField, ""); });
            OnlineFlow.ChatChanged += ShowChat;
            ShowChat();
            ShowPlayers();
        }

        private void ShowChat()
        {
            if (chatText == null) return;
            chatText.text = string.Join("\n", OnlineFlow.Chat.Skip(Mathf.Max(0, OnlineFlow.Chat.Count - 12)));
        }

        private void ShowPlayers()
        {
            if (playersText == null) return;
            var lines = new List<string>();
            foreach (var kv in Net.Roster)
            {
                string tag = kv.Key == 0 ? " <color=#FFD95A>HOST</color>" : "";
                string me = kv.Key == Net.MyId ? " <color=#8FD3FF>(YOU)</color>" : "";
                string ping = kv.Key == 0 && Net.IsHost ? "" : kv.Key == 0 ? $"  {Mathf.RoundToInt(Net.PingMs)} MS" : $"  {kv.Value.ping} MS";
                if (Net.IsClient && kv.Key == Net.MyId) ping = $"  {Mathf.RoundToInt(Net.PingMs)} MS";
                if (Net.IsClient && kv.Key == 0) ping = "";
                lines.Add($"{kv.Value.name.Replace("<", "‹").ToUpperInvariant()}{tag}{me}<color=#BFE6FF>{ping}</color>");
            }
            playersText.text = string.Join("\n", lines);
            if (info != null)
                info.text = $"CODE <color=#FFFFFF>{Net.Lobby.Code}</color>  ·  {(Net.Lobby.Public ? "PUBLIC" : "PRIVATE")}  ·  {Mathf.Max(1, Net.Roster.Count)}/{Net.Lobby.MaxPCs} PCS";
            heading.text = Net.Lobby.Name.Replace("<", "‹").ToUpperInvariant() + "  <size=60%>" + Format.Replace("v", " VS ").ToUpperInvariant() + "</size>";
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            OnlineFlow.ChatChanged -= ShowChat;
            if (open == this) open = null;
        }

        // ------------------------------------------------------------ online

        public static void PeerJoined() { if (open != null) { open.lastSent = null; open.Refresh(); } }

        // a friend's PC left: its players leave their seats
        public static void PeerLeft(int pc)
        {
            if (open == null) return;
            for (int i = 0; i < MaxPlayers; i++)
                if (open.input[i]?.Kind == InputKind.Remote && Net.PcOf(open.input[i].DeviceId) == pc) { open.input[i] = null; open.ready[i] = false; }
            open.Refresh();
        }

        // the session ended: the lobby screen closes (the browser opens with the reason)
        public static void SessionEnded(string reason)
        {
            if (open == null || !open.lobby) return;
            open.Close();
            LobbyBrowser.Open();
        }

        private void LeaveLobby()
        {
            Net.Stop(Net.IsHost ? "THE HOST CLOSED THE LOBBY" : null);
            if (open == this) { Close(); LobbyBrowser.Open(); }
        }

        // host -> friends: the whole join screen, whenever it changes
        private void SendState()
        {
            if (!(Net.IsHost && Net.HasFriends)) return;
            var w = new NetWriter(Msg.Menu).U8(format).I(team[0]).I(team[1]);
            for (int i = 0; i < MaxPlayers; i++) w.S(input[i]?.Label ?? "").B(ready[i]);
            string key = System.Convert.ToBase64String(w.Bytes());
            if (key == lastSent) return;
            lastSent = key;
            Net.Send(w);
        }

        public static void MirrorState(NetReader r)
        {
            if (open == null || !Net.IsClient) return;
            open.format = r.U8(); open.team[0] = r.I(); open.team[1] = r.I();
            for (int i = 0; i < MaxPlayers; i++)
            {
                string label = r.S(); bool rd = r.B();
                open.input[i] = label.Length > 0 ? new SlotInput { Kind = InputKind.Remote, Label = label } : null;
                open.ready[i] = rd;
            }
            open.Refresh();
        }

        // development (-ovt-host / -ovt-join, DevKeys on)
        public static bool DevHost(string format, int seats, int pcs, bool isPublic = false)
        {
            if (classicRef == null) return false;
            Net.StartHost(new LobbyOptions { Name = "Dev lobby", Public = isPublic, MaxPCs = pcs });
            OpenLobby();
            if (open == null) return false;
            open.SetFormat(System.Math.Max(0, System.Array.IndexOf(VersusSetup.Formats, format)));
            for (int i = 0; i < seats && i < open.Count; i++)
                open.input[i] = new SlotInput { Kind = VersusInput.KeyboardSets[i].kind, Label = VersusInput.KeyboardSets[i].label };
            open.Refresh();
            return true;
        }

        public static bool DevReadyWhenOthersAre()
        {
            if (open == null) return false;
            for (int i = 0; i < open.Count; i++) if (open.input[i] == null || (open.input[i].Kind == InputKind.Remote && !open.ready[i])) return false;
            for (int i = 0; i < open.Count; i++) open.ready[i] = true;
            open.Refresh();
            open.TryStart();
            return true;
        }

        // a friend's PC: the host started the match
        public static bool StartFromHost()
        {
            if (open == null) return false;
            open.starting = true;
            var gm = GameManager.Instance;
            var title = titleOf(open.classic);
            if (title != null) open.classic.StartCoroutine(title.StartGame());
            gm.ResetGame();
            PauseController.paused = false;
            open.classic.StartCoroutine(gm.LoadGame());
            open.Close(keepMenuOff: true);
            return true;
        }

        private void SetFormat(int f)
        {
            if (starting || f == format || Mirror) return;
            format = f;
            // players beyond the new mode's seats leave; everyone un-readies (seats may have moved sides)
            for (int i = 0; i < MaxPlayers; i++) { ready[i] = false; if (i >= Count) input[i] = null; }
            Plugin.Log.LogInfo("Versus: mode " + Format);
            if (Net.IsHost) { Lobbies.SetMode(Format); if (lobby) lastLobbyFormat = format; }
            Refresh();
        }

        private string TeamLabel(int side) => VersusState.TeamName(side, teams[side][team[side]]).ToUpperInvariant() + (side == 0 && team[0] == 0 ? "  <size=60%>(YOUR LOOK)</size>" : "");

        private Color TeamColour(int side)
        {
            string key = teams[side][team[side]];
            var def = Generated.Teams.All.FirstOrDefault(t => t.Id == key);
            if (def != null) return Vfx.Hex(def.Jersey);
            return side == 0 ? new Color(0.16f, 0.72f, 1f) : new Color(0.55f, 0.6f, 0.7f);
        }

        private static string RoleName(string role) => role == "setter" ? "SETTER" : role == "partner" ? "PARTNER" : "HITTER";

        private void Refresh()
        {
            if (lobby) ShowPlayers();
            else heading.text = "VERSUS  <size=60%>" + Format.Replace("v", " VS ").ToUpperInvariant() + "</size>";
            for (int i = 0; i < 3; i++) formatTabs[i].color = i == format ? new Color(0.96f, 0.55f, 0.12f, 1f) : new Color(0.16f, 0.6f, 0.86f, 1f);
            var seats = Seats;
            for (int side = 0; side < 2; side++)
            {
                var mine = Enumerable.Range(0, seats.Length).Where(i => seats[i].side == side).ToList();
                for (int r = 0; r < 2; r++)
                {
                    string text;
                    Color box = new Color(0.05f, 0.38f, 0.6f, 1f);
                    if (r < mine.Count)
                    {
                        int i = mine[r];
                        string who = $"P{i + 1}  <size=70%>{RoleName(seats[i].role)}</size>";
                        text = input[i] == null ? $"{who}\n<size=80%>PRESS A BUTTON TO JOIN</size>" :
                               ready[i] ? $"{who}\n<size=80%>{input[i].Label} · <color=#FFD95A>READY!</color></size>" :
                                          $"{who}\n<size=80%>{input[i].Label} · JUMP WHEN READY</size>";
                        if (ready[i]) box = new Color(0.1f, 0.45f, 0.35f, 1f);
                    }
                    else
                    {
                        text = "AI  <size=70%>SETTER</size>\n<size=80%>COMPUTER</size>";
                        box = new Color(0.04f, 0.27f, 0.42f, 1f);
                    }
                    rows[side, r].text = text;
                    rowBoxes[side, r].color = box;
                }
                if (team[side] >= teams[side].Count) team[side] = 0;
                bool joined = input[Leader(side)] != null;
                swatch[side].enabled = joined;
                teamText[side].text = joined ? "<  " + TeamLabel(side) + "  >" : "";
                var c = TeamColour(side);
                swatch[side].color = new Color(c.r * 0.7f, c.g * 0.7f, c.b * 0.7f, 1f);
            }
            SendState();
        }

        // ------------------------------------------------------------ input

        private void Update()
        {
            if (starting || Time.unscaledTime - openedAt < 0.3f) return;
            if (FieldKeys() || Typing) return;                                      // the keyboard is typing in the chat
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { if (lobby) LeaveLobby(); else Close(); return; }
            if (lobby)
            {
                // a friend whose lobby screen opened late (after a match) still gets the seats
                if (Net.IsHost && Time.unscaledTime > nextState) { nextState = Time.unscaledTime + 2f; lastSent = null; SendState(); }
                if (kb != null && kb.tKey.wasPressedThisFrame && chatField != null) { Focus(chatField); return; }
                if (Time.unscaledTime > nextPlayers) { nextPlayers = Time.unscaledTime + 0.5f; ShowPlayers(); }
                if (status != null && status.text != Net.Status) status.text = Net.Status;
            }
            if (Mirror) return;                                                     // the host runs the seats; our keys travel to it
            var seats = Seats;
            for (int i = 0; i < Count; i++)
            {
                if (input[i] == null) continue;
                var s = VersusInput.Read(input[i]);
                if (s.Back) { if (ready[i]) ready[i] = false; else if (input[i].Kind == InputKind.Gamepad || input[i].Kind == InputKind.Remote) Leave(i); Refresh(); return; }
                int side = seats[i].side;
                int dx = (s.Right ? 1 : 0) - (s.Left ? 1 : 0);
                if (dx != 0 && !ready[i] && Leader(side) == i) { team[side] = (team[side] + dx + teams[side].Count) % teams[side].Count; Refresh(); }
                if (s.Down && i == 0 && !ready[0]) { SetFormat((format + 1) % VersusSetup.Formats.Length); return; }
                if (s.Up || s.Confirm)
                {
                    ready[i] = !ready[i];
                    Refresh();
                    if (Enumerable.Range(0, Count).All(j => input[j] != null && ready[j])) { TryStart(); return; }
                }
            }
            if (Net.IsClient) return;                                               // still connecting: our keys wait for the host
            var join = VersusInput.DetectJoin(input);
            if (join != null && !input.Any(x => x != null && x.Same(join)))
            {
                int free = System.Array.FindIndex(input, x => x == null);
                if (free >= 0 && free < Count) { input[free] = join; Plugin.Log.LogInfo($"Versus: P{free + 1} joined on {join.Label}"); Refresh(); }
            }
        }

        // a gamepad player backs out: later players keep their numbers (the seat stays open for someone to join)
        private void Leave(int i)
        {
            Plugin.Log.LogInfo($"Versus: P{i + 1} left");
            input[i] = null;
            ready[i] = false;
        }

        private void TryStart()
        {
            if (starting || Mirror || Enumerable.Range(0, Count).Any(i => input[i] == null)) return;
            starting = true;
            var setup = VersusSetup.Create(Format, input.Take(Count).ToList(), teams[0][team[0]], teams[1][team[1]]);
            VersusState.NewMatch(setup);
            var gm = GameManager.Instance;
            gm.numberOfPlayers = Format == "2v2" ? 2 : 1;     // 2v2: the game's own co-op pair on the left
            OnlineFlow.SendLoad(true);
            var title = titleOf(classic);
            if (title != null) classic.StartCoroutine(title.StartGame());
            gm.ResetGame();
            PauseController.paused = false;
            classic.StartCoroutine(gm.LoadGame());
            Close(keepMenuOff: true);
        }

        private void Close(bool keepMenuOff = false)
        {
            if (!keepMenuOff)
            {
                foreach (var c in disabled) if (c != null) c.enabled = true;
                if (titleGroup != null)
                {
                    titleGroup.enabled = true;
                    foreach (var b in titleGroup.buttons.Where(b => b != null)) { b.interactable = true; b.wasClicked = false; }
                }
            }
            if (open == this) open = null;
            Destroy(gameObject);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Patches;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Online
{
    // Title screen MULTIPLAYER: every Overtime lobby on Steam (worldwide), search by code, create a lobby (name, public
    // or private, 2-4 PCs), or join a host by IP. Joining or creating opens the lobby screen (VersusMenu, lobby mode).
    internal class LobbyBrowser : OvertimePanel
    {
        private static LobbyBrowser open;
        public static bool IsOpen => open != null;

        private ButtonGroup titleGroup;
        private readonly List<Collider2D> disabled = new List<Collider2D>();
        private bool sameVersion = true, showFull;
        private bool makePublic = true;
        private int maxPcs = 2;
        private Field nameField, codeField, ipField;
        private TextMeshPro status, maxText;
        private List<LobbyEntry> lobbies = new List<LobbyEntry>();
        private int scroll;
        private const int Rows = 8;                      // rows of 58 px from y 290
        private readonly SpriteRenderer[] rowBoxes = new SpriteRenderer[Rows];
        private readonly TextMeshPro[,] cells = new TextMeshPro[Rows, 5];
        private bool searching, joining;
        private float joinStarted;

        public static void Open()
        {
            if (open != null || VersusMenu.ClassicButton == null) return;
            var go = new GameObject("Overtime Lobby Browser");
            open = go.AddComponent<LobbyBrowser>();
            if (!open.InitKit(200)) { Destroy(go); open = null; return; }
            open.Build();
        }

        private void Build()
        {
            titleGroup = VersusMenu.ClassicButton.GetComponentInParent<ButtonGroup>(true);
            if (titleGroup != null)
            {
                titleGroup.enabled = false;
                foreach (var b in titleGroup.buttons.Where(b => b != null))
                    foreach (var c in b.GetComponents<Collider2D>().Where(c => c.enabled)) { c.enabled = false; disabled.Add(c); }
            }
            Frame("ONLINE", () => Close());
            // left: the lobbies
            Rect("List", 70, 160, 1060, 800, new Color(0.03f, 0.11f, 0.2f, 1f), 2);
            IconBtn("refresh", "REFRESH", 90, 176, 270, 222, () => Refresh(null), 20);
            Toggle("SAME VERSION", P(380, 199), S(200, 46), () => sameVersion, v => { sameVersion = v; Refresh(null); }, F(18));
            Toggle("SHOW FULL", P(580, 199), S(180, 46), () => showFull, v => { showFull = v; Refresh(null); }, F(18));
            codeField = TextField(P(780, 199), S(190, 46), "CODE", 6, c => Refresh(c), upper: true);
            IconBtn("globe", "FIND", 890, 176, 1040, 222, () => Refresh(codeField.Value), 20);
            float[] cx = { 110, 640, 760, 880, 990 };
            string[] heads = { "LOBBY NAME", "VERSION", "MODE", "STATUS", "PCS" };
            for (int c = 0; c < 5; c++)
                if (c == 0) TL(heads[c], cx[c], 250, 20, UiArt.Gold, 400); else TC(heads[c], cx[c], 250, 20, UiArt.Gold, 120);
            for (int i = 0; i < Rows; i++)
            {
                int row = i;
                float ry = 290f + i * 58f;
                rowBoxes[i] = Shade(Btn("", 86, ry - 25, 1000, ry + 25, () => Pick(row)), new Color(0.094f, 0.275f, 0.46f, 1f));
                for (int c = 0; c < 5; c++)
                    cells[i, c] = c == 0 ? TL("", cx[c], ry, 24, UiArt.Ink, 500) : TC("", cx[c], ry, 22, UiArt.Ink, 120);
            }
            Btn("^", 1010, 266, 1048, 312, () => Scroll(-1), 22);
            Btn("v", 1010, 690, 1048, 736, () => Scroll(1), 22);
            status = TC("", 565, 772, 22, UiArt.Gold, 940);

            // right: make a lobby, join by IP
            Rect("Create", 1090, 160, 1530, 560, new Color(0.03f, 0.11f, 0.2f, 1f), 2);
            TC("CREATE A LOBBY", 1310, 196, 32, UiArt.Gold, 420);
            nameField = TextField(P(1310, 256), S(400, 50), "LOBBY NAME", 28, n => Create());
            Toggle("PUBLIC", P(1220, 326), S(220, 50), () => makePublic, v => makePublic = v, F(22));
            Btn("<", 1340, 304, 1380, 348, () => SetMax(-1), 24);
            maxText = TC("", 1427, 326, 24, UiArt.Ink, 90);
            Btn(">", 1474, 304, 1514, 348, () => SetMax(1), 24);
            SetMax(0);
            GoldSign("CREATE", 1130, 400, 1490, 470, Create, 36, "plus");
            TW("PUBLIC LOBBIES SHOW IN EVERYONE'S LIST; PRIVATE ONES ARE JOINED BY CODE OR STEAM INVITE", 1310, 515, 17, new Color(0.78f, 0.88f, 1f), 400, 60);
            Rect("Ip", 1090, 580, 1530, 800, new Color(0.03f, 0.11f, 0.2f, 1f), 2);
            TC("JOIN BY IP", 1310, 614, 30, UiArt.Gold, 420);
            ipField = TextField(P(1310, 672), S(400, 50), "ADDRESS OR ADDRESS:PORT", 40, a => JoinIp(), Plugin.OnlineJoin.Value);
            IconBtn("play", "JOIN", 1200, 712, 1420, 762, JoinIp, 24);
            PadHint("A: CHOOSE (A ON A TEXT BOX: KEYBOARD)   B: BACK   LB / RB: SCROLL", P(640, 869), 900 / 25f * KX);
            navKeys = true;
            focus = 0;
            if (OnlineFlow.PendingMessage != null) { status.text = OnlineFlow.PendingMessage; OnlineFlow.PendingMessage = null; }
            Show();
            if (Lobbies.Available) Refresh(null, keepStatus: status.text.Length > 0);
            else status.text = (status.text.Length > 0 ? status.text + "  ·  " : "") + "STEAM ISN'T RUNNING: JOIN BY IP ONLY";
        }

        private void SetMax(int d)
        {
            maxPcs = Mathf.Clamp(maxPcs + d, 2, 4);
            maxText.text = $"{maxPcs} PCS";
        }

        // ------------------------------------------------------------ the list

        private void Refresh(string code, bool keepStatus = false)
        {
            if (searching) return;
            if (!Lobbies.RequestList(sameVersion, showFull, code, OnList)) { status.text = Lobbies.LastError; return; }
            searching = true;
            if (!keepStatus) status.text = string.IsNullOrEmpty(code) ? "SEARCHING..." : $"LOOKING FOR {code.ToUpperInvariant()}...";
        }

        private void OnList(List<LobbyEntry> list)
        {
            if (this == null) return;
            searching = false;
            lobbies = list.OrderByDescending(l => l.SameVersion).ThenBy(l => l.Full).ThenByDescending(l => l.Pcs).ToList();
            scroll = 0;
            if (status.text.EndsWith("...")) status.text = lobbies.Count == 0 ? "NO LOBBIES FOUND · CREATE ONE BELOW" : $"{lobbies.Count} LOBB{(lobbies.Count == 1 ? "Y" : "IES")}";
            Show();
        }

        private void Scroll(int d)
        {
            scroll = Mathf.Clamp(scroll + d, 0, Mathf.Max(0, lobbies.Count - Rows));
            Show();
        }

        private void Show()
        {
            for (int i = 0; i < Rows; i++)
            {
                int k = scroll + i;
                bool has = k < lobbies.Count;
                rowBoxes[i].gameObject.SetActive(has);
                var e = has ? lobbies[k] : null;
                var c = has && !e.SameVersion ? new Color(1f, 0.6f, 0.55f) : Color.white;
                string[] v = has ? new[] { e.Name, e.Version.Split('/')[0], e.Mode.Replace("v", " VS ").ToUpperInvariant(), e.State, $"{e.Pcs}/{e.Max}" } : new string[5];
                for (int col = 0; col < 5; col++)
                {
                    cells[i, col].text = has ? v[col].Replace("<", "‹").Replace(">", "›") : "";
                    cells[i, col].color = col == 3 && has && e.State != "LOBBY" ? UiArt.Gold : c;
                }
            }
        }

        private void Pick(int row)
        {
            int k = scroll + row;
            if (k >= lobbies.Count || joining) return;
            var e = lobbies[k];
            if (!e.SameVersion) { status.text = $"THAT LOBBY RUNS OVERTIME {e.Version.Split('/')[0]} (YOU HAVE {Plugin.Version})"; return; }
            if (e.Full) { status.text = "THAT LOBBY IS FULL"; return; }
            if (e.State != "LOBBY") { status.text = "A MATCH IS RUNNING IN THAT LOBBY: TRY AGAIN WHEN IT ENDS"; return; }
            joining = true; joinStarted = Time.unscaledTime;
            status.text = $"JOINING {e.Name.ToUpperInvariant()}...";
            Lobbies.Join(e.Id);
        }

        // ------------------------------------------------------------ hosting and joining

        private void Create()
        {
            if (Net.Online) return;
            Net.StartHost(new LobbyOptions { Name = nameField.Value, Public = makePublic, MaxPCs = maxPcs });
        }

        private void JoinIp()
        {
            if (Net.Online || ipField.Value.Trim().Length == 0) return;
            Plugin.OnlineJoin.Value = ipField.Value.Trim();
            joining = true; joinStarted = Time.unscaledTime;
            Net.JoinIp(ipField.Value);
            status.text = Net.Status;
        }

        private void Update()
        {
            bool typed = FieldKeys() || NavUpdate();
            // in a session: the lobby screen takes over
            if (Net.IsHost || (Net.IsClient && Net.Connected)) { Close(); VersusMenu.OpenLobby(); return; }
            if (joining)
            {
                if (Net.IsClient) status.text = Net.Status;
                else if (Lobbies.LastError.Length > 0) { status.text = Lobbies.LastError; joining = false; }
                else if (!Net.Online && Time.unscaledTime - joinStarted > 1f && OnlineFlow.PendingMessage != null) { status.text = OnlineFlow.PendingMessage; OnlineFlow.PendingMessage = null; joining = false; }
                else if (Time.unscaledTime - joinStarted > 15f) { status.text = "NO ANSWER"; joining = false; }
            }
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null) { float s = mouse.scroll.ReadValue().y; if (s > 0.1f) Scroll(-1); else if (s < -0.1f) Scroll(1); }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (!typed && !Typing && kb != null && kb.escapeKey.wasPressedThisFrame) Close();
        }

        // the title buttons come back (the lobby screen, if it opens next, turns them off again itself)
        private void Close()
        {
            foreach (var c in disabled) if (c != null) c.enabled = true;
            if (titleGroup != null)
            {
                titleGroup.enabled = true;
                foreach (var b in titleGroup.buttons.Where(b => b != null)) { b.interactable = true; b.wasClicked = false; }
            }
            open = null;
            Destroy(gameObject);
        }

        protected override void OnNavBack() => Close();
        protected override void OnNavTab(int dir) => Scroll(dir * Rows);

        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }
    }
}

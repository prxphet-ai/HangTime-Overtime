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
        private const int Rows = 8;
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
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            float pw = W * 0.9f, ph = H * 0.88f, top = ph / 2f, left = -pw / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            Text("MULTIPLAYER  <size=55%>ONLINE VERSUS</size>", new Vector2(0f, top - 1.8f), 1.2f, pw - 2f, Color.white);

            // top row: refresh, filters, search by code
            float y = top - 4.2f;
            Button("REFRESH", new Vector2(left + pw * 0.08f, y), new Vector2(pw * 0.12f, 2f), () => Refresh(null), 0.55f);
            Toggle("SAME VERSION", new Vector2(left + pw * 0.23f, y), new Vector2(pw * 0.16f, 2f), () => sameVersion, v => { sameVersion = v; Refresh(null); });
            Toggle("SHOW FULL", new Vector2(left + pw * 0.38f, y), new Vector2(pw * 0.12f, 2f), () => showFull, v => { showFull = v; Refresh(null); });
            codeField = TextField(new Vector2(left + pw * 0.66f, y), new Vector2(pw * 0.22f, 2f), "SEARCH BY CODE", 6, c => Refresh(c), upper: true);
            Button("SEARCH", new Vector2(left + pw * 0.86f, y), new Vector2(pw * 0.14f, 2f), () => Refresh(codeField.Value), 0.55f);

            // the list
            float listTop = y - 2.4f;
            float[] cx = { 0.05f, 0.45f, 0.6f, 0.72f, 0.88f };
            string[] heads = { "LOBBY NAME", "VERSION", "MODE", "STATUS", "PCS" };
            for (int c = 0; c < 5; c++)
                Text(heads[c], new Vector2(left + pw * cx[c] + (c == 0 ? pw * 0.17f : 0f), listTop), 0.5f, c == 0 ? pw * 0.36f : pw * 0.14f, new Color(0.85f, 0.95f, 1f), c == 0 ? TextAlignmentOptions.Left : TextAlignmentOptions.Center);
            float rh = 2.1f;
            for (int i = 0; i < Rows; i++)
            {
                int row = i;
                float ry = listTop - 2f - i * rh;
                rowBoxes[i] = Button("", new Vector2(0f, ry), new Vector2(pw - 2f, rh - 0.2f), () => Pick(row), 0.5f);
                for (int c = 0; c < 5; c++)
                    cells[i, c] = Text("", new Vector2(left + pw * cx[c] + (c == 0 ? pw * 0.17f : 0f), ry), 0.5f, c == 0 ? pw * 0.36f : pw * 0.14f, Color.white,
                                       c == 0 ? TextAlignmentOptions.Left : TextAlignmentOptions.Center, o: 9);
            }
            float listBottom = listTop - 2f - Rows * rh;
            Button("^", new Vector2(pw / 2f - 1.6f, listTop - 2f), new Vector2(1.6f, 1.8f), () => Scroll(-1), 0.6f);
            Button("v", new Vector2(pw / 2f - 1.6f, listBottom + rh), new Vector2(1.6f, 1.8f), () => Scroll(1), 0.6f);
            status = Text("", new Vector2(0f, listBottom + 0.6f), 0.55f, pw - 4f, new Color(1f, 0.85f, 0.35f));

            // create a lobby
            float by = -ph / 2f + 4.4f;
            Button("CREATE LOBBY", new Vector2(left + pw * 0.1f, by), new Vector2(pw * 0.17f, 2.2f), Create, 0.6f);
            nameField = TextField(new Vector2(left + pw * 0.34f, by), new Vector2(pw * 0.28f, 2.2f), "LOBBY NAME", 28, n => Create());
            Toggle("PUBLIC", new Vector2(left + pw * 0.57f, by), new Vector2(pw * 0.14f, 2.2f), () => makePublic, v => makePublic = v);
            Button("<", new Vector2(left + pw * 0.67f, by), new Vector2(1.6f, 2.2f), () => SetMax(-1), 0.6f);
            maxText = Text("", new Vector2(left + pw * 0.735f, by), 0.55f, pw * 0.1f, Color.white);
            Button(">", new Vector2(left + pw * 0.80f, by), new Vector2(1.6f, 2.2f), () => SetMax(1), 0.6f);
            SetMax(0);

            // join by IP, back
            float iy = -ph / 2f + 1.7f;
            ipField = TextField(new Vector2(left + pw * 0.2f, iy), new Vector2(pw * 0.3f, 2.2f), "HOST IP (ADDRESS OR ADDRESS:PORT)", 40, a => JoinIp(), Plugin.OnlineJoin.Value);
            Button("JOIN BY IP", new Vector2(left + pw * 0.46f, iy), new Vector2(pw * 0.17f, 2.2f), JoinIp, 0.6f);
            Button("BACK", new Vector2(pw / 2f - pw * 0.1f, iy), new Vector2(pw * 0.15f, 2.2f), () => Close(), 0.6f);

            PadHint("A: CHOOSE (A ON A TEXT BOX: KEYBOARD)   ·   B: BACK   ·   LB / RB: SCROLL", new Vector2(0f, top - 2.9f), pw - 2f);
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
                    cells[i, col].color = UiArt.Paint(col == 3 && has && e.State != "LOBBY" ? new Color(1f, 0.85f, 0.35f) : c);
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

using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // Title screen VERSUS: choose 1 VS 1, 1 VS 2 or 2 VS 2; each player presses a button on their own device to join (join
    // order fills the seats: left side first), each side's first player picks its team, everyone readies up, and the
    // match starts when every seat is filled and ready.
    internal class VersusMenu : OvertimePanel
    {
        private static readonly AccessTools.FieldRef<ButtonController, OnClick> onClickOf = AccessTools.FieldRefAccess<ButtonController, OnClick>("onClick");
        private static readonly AccessTools.FieldRef<TitleButton, TitleController> titleOf = AccessTools.FieldRefAccess<TitleButton, TitleController>("title");
        private static VersusMenu open;
        private const int MaxPlayers = 4;

        private TitleButton classic;
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
        private TextMeshPro heading;
        private float openedAt;
        private bool starting;

        private string Format => VersusSetup.Formats[format];
        private (int side, string role)[] Seats => VersusSetup.Seats(Format);
        private int Count => Seats.Length;
        private int Leader(int side) => System.Array.FindIndex(Seats, x => x.side == side);   // seat that picks the side's team

        // ------------------------------------------------------------ the title button

        public static void AddTitleButton(GameObject classicButton)
        {
            if (classicButton == null) return;
            var group = classicButton.GetComponentInParent<ButtonGroup>(true);
            var customize = group?.buttons.FirstOrDefault(b => b != null && b.name == "Customize Button");
            if (group == null || customize == null || group.buttons.Any(b => b != null && b.name == "Versus Button")) return;
            var b = Instantiate(customize.gameObject, customize.transform.parent);
            b.name = "Versus Button";
            foreach (var m in b.GetComponents<MonoBehaviour>().Where(m => m is PopupClick)) DestroyImmediate(m);
            var click = b.AddComponent<PopupClick>();
            var tb = classicButton.GetComponent<TitleButton>();
            click.Action = () => Open(tb);
            var ctrl = b.GetComponent<ButtonController>();
            onClickOf(ctrl) = click;
            ctrl.group = group;
            MenuPatches.SetButtonLabels(b, "VERSUS", "1V1 · 1V2 · 2V2");
            b.transform.position = customize.transform.position + new Vector3(0f, 3.3f, 0f);
            Traverse.Create(ctrl).Field("originalScale").SetValue(b.transform.localScale.x);
            group.buttons.Insert(group.buttons.IndexOf(customize), ctrl);
        }

        private static void Open(TitleButton classicTb)
        {
            if (open != null) return;
            var go = new GameObject("Overtime Versus Menu");
            open = go.AddComponent<VersusMenu>();
            open.classic = classicTb;
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

            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.6f), 0);
            float pw = W * 0.86f, ph = H * 0.84f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            heading = Text("VERSUS", new Vector2(0f, ph / 2f - 2f), 1.4f, pw - 2f, Color.white);
            Text($"ROUNDS TO {Generated.VersusRules.PointsPerRound} POINTS · FIRST TO {Generated.VersusRules.RoundsToWin} ROUNDS · THE LOSING SIDE PICKS AN UPGRADE EACH ROUND",
                new Vector2(0f, ph / 2f - 3.8f), 0.45f, pw - 2f, new Color(0.85f, 0.95f, 1f));
            // mode tabs (mouse, or player 1's DOWN key)
            float tw = pw * 0.14f;
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                formatTabs[i] = Button(VersusSetup.Formats[i].Replace("v", " VS "), new Vector2((i - 1) * tw * 1.1f, ph / 2f - 6f), new Vector2(tw, 2.2f), () => SetFormat(idx), 0.6f);
            }
            for (int side = 0; side < 2; side++)
            {
                float x = (side == 0 ? -1f : 1f) * pw * 0.24f;
                Box("Column", new Vector2(x, -1.6f), new Vector2(pw * 0.42f, ph * 0.56f), new Color(0.03f, 0.3f, 0.5f, 1f), 2);
                sideTitle[side] = Text(side == 0 ? "LEFT SIDE" : "RIGHT SIDE", new Vector2(x, ph * 0.17f), 0.8f, pw * 0.4f, Color.white);
                for (int r = 0; r < 2; r++)
                {
                    float y = ph * 0.06f - r * ph * 0.13f;
                    rowBoxes[side, r] = Box("Seat", new Vector2(x, y), new Vector2(pw * 0.38f, ph * 0.11f), new Color(0.05f, 0.38f, 0.6f, 1f), 3);
                    rows[side, r] = Text("", new Vector2(x, y), 0.55f, pw * 0.36f, Color.white, height: ph * 0.1f, wrap: true);
                }
                swatch[side] = Box("Team colour", new Vector2(x, -ph * 0.25f), new Vector2(pw * 0.3f, 2.6f), Color.white, 3);
                teamText[side] = Text("", new Vector2(x, -ph * 0.25f), 0.7f, pw * 0.3f, Color.white);
            }
            Text("JOIN: PRESS A KEY ON  W A S D,  ARROWS,  I J K L,  NUMPAD 8 4 5 6  OR  A ON A GAMEPAD   ·   LEFT / RIGHT: TEAM (EACH SIDE'S FIRST PLAYER)   ·   JUMP: READY   ·   DOWN (P1): MODE   ·   ESC: BACK",
                new Vector2(0f, -ph / 2f + 3.8f), 0.4f, pw - 2f, new Color(0.85f, 0.95f, 1f), height: 2.4f, wrap: true);
            Button("BACK", new Vector2(-pw * 0.12f, -ph / 2f + 1.6f), new Vector2(pw * 0.18f, 2.4f), () => Close(), 0.7f);
            Button("START", new Vector2(pw * 0.12f, -ph / 2f + 1.6f), new Vector2(pw * 0.18f, 2.4f), () => TryStart(), 0.7f);
            Refresh();
        }

        private void SetFormat(int f)
        {
            if (starting || f == format) return;
            format = f;
            // players beyond the new mode's seats leave; everyone un-readies (seats may have moved sides)
            for (int i = 0; i < MaxPlayers; i++) { ready[i] = false; if (i >= Count) input[i] = null; }
            Plugin.Log.LogInfo("Versus: mode " + Format);
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
            heading.text = "VERSUS  <size=60%>" + Format.Replace("v", " VS ").ToUpperInvariant() + "</size>";
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
                bool joined = input[Leader(side)] != null;
                swatch[side].enabled = joined;
                teamText[side].text = joined ? "<  " + TeamLabel(side) + "  >" : "";
                var c = TeamColour(side);
                swatch[side].color = new Color(c.r * 0.7f, c.g * 0.7f, c.b * 0.7f, 1f);
            }
        }

        // ------------------------------------------------------------ input

        private void Update()
        {
            if (starting || Time.unscaledTime - openedAt < 0.3f) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Close(); return; }
            var seats = Seats;
            for (int i = 0; i < Count; i++)
            {
                if (input[i] == null) continue;
                var s = VersusInput.Read(input[i]);
                if (s.Back) { if (ready[i]) ready[i] = false; else if (input[i].Kind == InputKind.Gamepad) Leave(i); Refresh(); return; }
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
            var join = VersusInput.DetectJoin();
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
            if (starting || Enumerable.Range(0, Count).Any(i => input[i] == null)) return;
            starting = true;
            var setup = VersusSetup.Create(Format, input.Take(Count).ToList(), teams[0][team[0]], teams[1][team[1]]);
            VersusState.NewMatch(setup);
            var gm = GameManager.Instance;
            gm.numberOfPlayers = Format == "2v2" ? 2 : 1;     // 2v2: the game's own co-op pair on the left
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
            open = null;
            Destroy(gameObject);
        }
    }
}

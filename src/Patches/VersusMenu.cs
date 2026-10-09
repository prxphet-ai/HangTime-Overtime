using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // Title screen VERSUS: each player presses a button on their own device to join, picks a team with it, and
    // readies up; the match starts when both are ready.
    internal class VersusMenu : OvertimePanel
    {
        private static readonly AccessTools.FieldRef<ButtonController, OnClick> onClickOf = AccessTools.FieldRefAccess<ButtonController, OnClick>("onClick");
        private static readonly AccessTools.FieldRef<TitleButton, TitleController> titleOf = AccessTools.FieldRefAccess<TitleButton, TitleController>("title");
        private static VersusMenu open;

        private TitleButton classic;
        private ButtonGroup titleGroup;
        private readonly List<Collider2D> disabled = new List<Collider2D>();
        private readonly SlotInput[] input = new SlotInput[2];
        private readonly int[] team = new int[2];
        private readonly bool[] ready = new bool[2];
        private readonly List<string>[] teams = { new List<string>(), new List<string>() };
        private readonly TextMeshPro[] status = new TextMeshPro[2], teamText = new TextMeshPro[2];
        private readonly SpriteRenderer[] swatch = new SpriteRenderer[2], columns = new SpriteRenderer[2];
        private float openedAt;
        private bool starting;

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
            MenuPatches.SetButtonLabels(b, "VERSUS", "1 VS 1");
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
            Text("VERSUS  <size=60%>1 VS 1</size>", new Vector2(0f, ph / 2f - 2f), 1.4f, pw - 2f, Color.white);
            Text($"ROUNDS TO {Generated.VersusRules.PointsPerRound} POINTS · FIRST TO {Generated.VersusRules.RoundsToWin} ROUNDS · THE LOSER OF EACH ROUND PICKS AN UPGRADE",
                new Vector2(0f, ph / 2f - 4f), 0.45f, pw - 2f, new Color(0.85f, 0.95f, 1f));
            for (int side = 0; side < 2; side++)
            {
                float x = (side == 0 ? -1f : 1f) * pw * 0.24f;
                columns[side] = Box("Column", new Vector2(x, -0.8f), new Vector2(pw * 0.42f, ph * 0.6f), new Color(0.03f, 0.3f, 0.5f, 1f), 2);
                Text($"PLAYER {side + 1}", new Vector2(x, ph * 0.22f), 1f, pw * 0.4f, Color.white);
                Text(side == 0 ? "LEFT SIDE" : "RIGHT SIDE", new Vector2(x, ph * 0.22f - 1.8f), 0.45f, pw * 0.4f, new Color(0.85f, 0.95f, 1f));
                status[side] = Text("", new Vector2(x, ph * 0.02f), 0.6f, pw * 0.38f, Color.white, height: 5f, wrap: true);
                swatch[side] = Box("Team colour", new Vector2(x, -ph * 0.14f), new Vector2(pw * 0.3f, 2.6f), Color.white, 3);
                teamText[side] = Text("", new Vector2(x, -ph * 0.14f), 0.7f, pw * 0.3f, Color.white);
            }
            Text("JOIN: PRESS A KEY ON  W A S D  OR  THE ARROW KEYS,  OR  A ON A GAMEPAD    ·    LEFT / RIGHT: TEAM    ·    JUMP: READY    ·    ESC: BACK",
                new Vector2(0f, -ph / 2f + 3.6f), 0.42f, pw - 2f, new Color(0.85f, 0.95f, 1f));
            Button("BACK", new Vector2(-pw * 0.12f, -ph / 2f + 1.6f), new Vector2(pw * 0.18f, 2.4f), () => Close(), 0.7f);
            Button("START", new Vector2(pw * 0.12f, -ph / 2f + 1.6f), new Vector2(pw * 0.18f, 2.4f), () => TryStart(), 0.7f);
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

        private void Refresh()
        {
            for (int side = 0; side < 2; side++)
            {
                status[side].text = input[side] == null ? "PRESS A BUTTON\nTO JOIN" :
                    ready[side] ? $"{input[side].Label}\n<color=#FFD95A>READY!</color>" : $"{input[side].Label}\n<size=70%>JUMP WHEN READY</size>";
                bool joined = input[side] != null;
                swatch[side].enabled = joined;
                teamText[side].text = joined ? "<  " + TeamLabel(side) + "  >" : "";
                var c = TeamColour(side);
                swatch[side].color = new Color(c.r * 0.7f, c.g * 0.7f, c.b * 0.7f, 1f);
                columns[side].color = ready[side] ? new Color(0.1f, 0.45f, 0.35f, 1f) : new Color(0.03f, 0.3f, 0.5f, 1f);
            }
        }

        // ------------------------------------------------------------ input

        private void Update()
        {
            if (starting || Time.unscaledTime - openedAt < 0.3f) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Close(); return; }
            for (int side = 0; side < 2; side++)
            {
                if (input[side] == null) continue;
                var s = VersusInput.Read(input[side]);
                if (s.Back) { if (ready[side]) ready[side] = false; else if (input[side].Kind == InputKind.Gamepad) input[side] = null; Refresh(); continue; }
                int dx = (s.Right ? 1 : 0) - (s.Left ? 1 : 0);
                if (dx != 0 && !ready[side]) { team[side] = (team[side] + dx + teams[side].Count) % teams[side].Count; Refresh(); }
                if (s.Up || s.Confirm) { ready[side] = !ready[side]; Refresh(); if (ready[0] && ready[1]) { TryStart(); return; } }
            }
            var join = VersusInput.DetectJoin();
            if (join != null && !input.Any(i => i != null && i.Same(join)))
            {
                int free = input[0] == null ? 0 : input[1] == null ? 1 : -1;
                if (free >= 0) { input[free] = join; Plugin.Log.LogInfo($"Versus: P{free + 1} joined on {join.Label}"); Refresh(); }
            }
        }

        private void TryStart()
        {
            if (starting || input[0] == null || input[1] == null) return;
            starting = true;
            var setup = VersusSetup.OneVsOne(input[0], input[1], teams[0][team[0]], teams[1][team[1]]);
            VersusState.NewMatch(setup);
            var gm = GameManager.Instance;
            gm.numberOfPlayers = 1;
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

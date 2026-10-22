using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Generated;
using HangtimeOvertime.Online;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Patches
{
    // PARTY CUP (the Versus / lobby screen): players join one by one (JUMP on a key set, A on a gamepad, a friend's device
    // online), RECEIVE / B leaves; the AI fills the bracket to 4 or 8; START plays the cup (Party, PartyFlow).
    internal class PartyMenu : OvertimePanel
    {
        private static PartyMenu open;
        public static bool IsOpen => open != null;
        private readonly List<Entrant> joined = new List<Entrant>();
        private static int size = 4;
        private TextMeshPro list, status;
        private float openedAt;

        public static void Open()
        {
            if (open != null || Net.IsClient) return;
            var go = new GameObject("Overtime Party");
            open = go.AddComponent<PartyMenu>();
            if (!open.InitKit(270)) { Destroy(go); open = null; return; }
            open.Build();
        }

        private void Build()
        {
            openedAt = Time.unscaledTime;
            foreach (Transform t in transform) Destroy(t.gameObject);
            ClearStops();
            navKeys = false;
            Box("Dim", Vector2.zero, new Vector2(W * 1.2f, H * 1.2f), new Color(0f, 0.1f, 0.2f, 0.65f), 0);
            float pw = W * 0.7f, ph = H * 0.88f, top = ph / 2f;
            Box("Panel", Vector2.zero, new Vector2(pw, ph), new Color(0.05f, 0.42f, 0.66f, 0.97f), 1);
            Text("PARTY CUP", new Vector2(0f, top - 1.8f), 1.1f, pw - 2f, Color.white);
            Text("A KNOCKOUT CUP OF 1 VS 1 VERSUS MATCHES (THE LOBBY RULES). JOIN: JUMP ON A KEY SET, A ON A GAMEPAD" + (Net.Online ? ", OR A FRIEND'S DEVICE" : "") +
                 ".  LEAVE: RECEIVE / B.  THE AI FILLS THE OPEN PLACES.", new Vector2(0f, top - 3.8f), 0.42f, pw - 3f, new Color(0.85f, 0.95f, 1f), height: 2.4f, wrap: true);
            Box("List", new Vector2(0f, top - 11f), new Vector2(pw - 4f, 11.5f), new Color(0.02f, 0.2f, 0.34f, 1f), 2);
            list = Text("", new Vector2(0f, top - 11f), 0.55f, pw - 6f, Color.white, TextAlignmentOptions.Top, height: 11f, wrap: true);
            float by = -ph / 2f + 1.8f;
            Button("SIZE: 4", new Vector2(-pw * 0.3f, by + 2.6f), new Vector2(pw * 0.18f, 2f), () => { size = 4; Refresh(); }, 0.55f);
            Button("SIZE: 8", new Vector2(-pw * 0.1f, by + 2.6f), new Vector2(pw * 0.18f, 2f), () => { size = 8; Refresh(); }, 0.55f);
            Button("RULES", new Vector2(pw * 0.3f, by + 2.6f), new Vector2(pw * 0.18f, 2f), () => RulesPanel.Open(), 0.55f);
            Button("BACK", new Vector2(-pw * 0.2f, by), new Vector2(pw * 0.22f, 2.3f), () => Destroy(gameObject), 0.6f);
            Button("START", new Vector2(pw * 0.2f, by), new Vector2(pw * 0.22f, 2.3f), StartCup, 0.6f);
            status = Text("", new Vector2(0f, by + 4.6f), 0.48f, pw - 2f, new Color(1f, 0.85f, 0.35f));
            Refresh();
        }

        private void Refresh()
        {
            if (joined.Count > size) size = 8;
            var lines = joined.Select((e, i) => $"{i + 1}. <noparse>{e.Name}</noparse>  <size=70%><color=#BFE6FF>{e.Input?.Label} · {VersusState.TeamName(1, e.Team).ToUpperInvariant()}</color></size>").ToList();
            int ai = Mathf.Max(0, size - joined.Count);
            if (ai > 0) lines.Add($"<color=#7F93A8>+ {ai} AI</color>");
            list.text = joined.Count == 0 ? "<color=#7F93A8>NOBODY YET: JUMP / A TO JOIN</color>" : string.Join("\n", lines);
            status.text = $"BRACKET OF {size}  ·  RULES: {LobbyRules.Current.Summary()}";
        }

        private void StartCup()
        {
            if (joined.Count < 1) { status.text = "AT LEAST ONE PLAYER, PLEASE"; return; }
            Party.Begin(joined.ToList(), size);
            Destroy(gameObject);
            PartyFlow.Next(true);
        }

        private string FreeTeam()
        {
            var used = new HashSet<string>(joined.Select(j => j.Team));
            var free = Teams.All.Where(t => !used.Contains(t.Id)).ToList();
            return (free.Count > 0 ? free[Random.Range(0, free.Count)] : Teams.All[0]).Id;
        }

        private void Update()
        {
            if (RulesPanel.IsOpen || BanPanel.IsOpen || Time.unscaledTime - openedAt < 0.3f) return;
            if (NavUpdate()) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { Destroy(gameObject); return; }
            var taken = joined.Select(j => j.Input).ToList();
            // leaving: a joined device's RECEIVE / BACK
            foreach (var e in joined.ToList())
            {
                var s = VersusInput.Read(e.Input);
                if (s.Down || (e.Input.Kind == InputKind.Gamepad && s.Back)) { joined.Remove(e); Rename(); Refresh(); return; }
            }
            if (joined.Count >= 8) return;
            var input = VersusInput.FreeKeysPressed(taken) ?? VersusInput.FreePadPressed(taken) ?? Net.RemoteJoin(taken);
            if (input == null) return;
            joined.Add(new Entrant { Input = input, Team = FreeTeam() });
            Rename();
            Refresh();
            Plugin.Log.LogInfo($"Party: joined on {input.Label}");
        }

        // P1, P2... in join order; a friend's players carry their PC's name
        private void Rename()
        {
            for (int i = 0; i < joined.Count; i++)
            {
                var e = joined[i];
                string pc = e.Input.Kind == InputKind.Remote ? Net.PeerById(Net.PcOf(e.Input.DeviceId))?.Name : (Net.Online ? Net.MyName : null);
                e.Name = string.IsNullOrEmpty(pc) ? $"P{i + 1}" : $"{pc.ToUpperInvariant()} (P{i + 1})";
            }
        }

        protected override void OnNavBack() => Destroy(gameObject);
        protected override void OnDestroy() { base.OnDestroy(); if (open == this) open = null; }

        // development (DevOnline -ovt-party): seat a device, start when enough have joined
        public static void DevJoin(SlotInput input) { if (open == null || open.joined.Any(j => j.Input.Same(input))) return; open.joined.Add(new Entrant { Input = input, Team = open.FreeTeam() }); open.Rename(); open.Refresh(); }
        public static int DevCount => open != null ? open.joined.Count : 0;
        public static void DevStart() { if (open != null) open.StartCup(); }
    }

    // the cup between matches: settles AI matches, shows the bracket (every PC), loads the next match
    internal static class PartyFlow
    {
        // from the menu (first) or after a match
        public static void Next(bool first)
        {
            bool more = Party.Advance();
            var model = Party.Model();
            OnlineFlow.SendBracket(model);
            BracketPanel.Show(model, false, () =>
            {
                if (!more) { Finish(); return; }
                Play(first);
            });
        }

        private static void Play(bool first)
        {
            var (l, r) = Party.Current;
            // the game's AI only plays the right side: a player against the AI is on the left
            Party.Swapped = l.AI && !r.AI;
            if (Party.Swapped) (l, r) = (r, l);
            var setup = VersusSetup.Create("1v1", new List<SlotInput> { l.Input, r.Input }, l.Team, r.Team);
            if (l.AI) { var s = setup.Slots.First(x => x.Side == 0 && x.Role == "hitter"); s.Control = SlotControl.AI; s.Input = null; }
            if (r.AI) { var s = setup.Slots.First(x => x.Side == 1 && x.Role == "hitter"); s.Control = SlotControl.AI; s.Input = null; }
            setup.Names[0] = l.Name; setup.Names[1] = r.Name;
            VersusState.NewMatch(setup);
            LobbyRules.GiveStartingPerks(System.Environment.TickCount);
            if (first) { OnlineFlow.SendLoad(true); VersusMenu.StartScene(1, closeMenu: true); }
            else VersusFlow.StartNewMatch();
        }

        // the match ended (VersusOverlay at match end, or a forfeit)
        public static void MatchOver(int winnerSide)
        {
            Party.MatchOver(Party.Swapped ? 1 - winnerSide : winnerSide);
            VersusOverlay.Close();
            Runner.Instance.StartCoroutine(After(1.2f, () => Next(false)));
        }

        private static void Finish()
        {
            Plugin.Log.LogInfo("Party: the cup is over" + (Party.Champion >= 0 ? ", " + Party.Entrants[Party.Champion].Name + " won" : ""));
            Party.End();
            if (Net.IsHost && Net.HasFriends) OnlineFlow.BackToLobby(); else VersusFlow.ToMenu();
        }

        // host: a friend dropped out mid-cup
        public static void PeerLeft(Peer p)
        {
            if (!Party.Active) return;
            int win = Party.PeerLeft(p.Id);
            if (win >= 0 && Party.Swapped) win = 1 - win;                      // the match's sides, not the bracket's
            if (win >= 0 && VersusState.Active)
            {
                Plugin.Log.LogInfo($"Party: {p.Name} left; the match goes to the other side");
                var gm = GameManager.Instance;
                if (gm != null) gm.done = true;
                VersusOverlay.Notice(null);
                Time.timeScale = 1f;
                MatchOver(win);
            }
        }

        private static IEnumerator After(float s, System.Action a) { yield return new WaitForSecondsRealtime(s); a(); }
    }
}

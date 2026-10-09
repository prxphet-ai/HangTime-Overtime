using System.Linq;
using HangtimeOvertime.Core;
using HangtimeOvertime.Engine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace HangtimeOvertime.Debugging
{
    // Test shortcuts, only when DevKeys is enabled in the config (off for players).
    //  F1 fire the lab perk's effects now (Shift: as the opponent)   F2 lab: next perk (Shift: previous), given alone from the next match
    //  F3 force the next opponent (cycles new teams, then random)   F4 player 1 serves now   F5 rally point to player (Shift: opponent) via the game's own scoring
    //  F6 win this match   F7 lose this match   F8 next win leads to the final match   F9 log state
    //  F10 give every Overtime perk   F11 perk screens show only Overtime perks   F12 next infinite match one tier harder
    internal class DevKeys : MonoBehaviour
    {
        public static bool OnlyOvertimeCards;
        private static int lab = -1;
        private static Gamepad testPad;

        private void Update()
        {
            var kb = Keyboard.current;
            var gm = GameManager.Instance;
            if (kb == null || gm == null) return;
            bool shift = kb.shiftKey.isPressed;
            // GameManager survives into the upgrade screen: scoring keys there would load it a second time and break its UI
            bool inMatch = SceneManager.GetActiveScene().name == "Game" && !gm.done;
            var perks = PerkRegistry.All.ToList();

            if (kb.f2Key.wasPressedThisFrame)
            {
                lab = (lab + (shift ? -1 : 1) + perks.Count) % perks.Count;
                gm.playerStats.techniques.RemoveAll(t => t is DataPerk);
                gm.playerStats.techniques.Add(perks[lab]);
                Plugin.Log.LogInfo($"Dev lab: {lab + 1}/{perks.Count} {perks[lab].Def.Title} ({perks[lab].Def.Element}, {string.Join("+", perks[lab].Def.Triggers)}) - active from the next match");
            }
            if (kb.f1Key.wasPressedThisFrame && lab >= 0)
            {
                int side = shift ? 1 : 0;
                Engine.Engine.DebugFire(perks[lab], side);
                Plugin.Log.LogInfo($"Dev lab: fired {perks[lab].Def.Title} as side {side}");
            }
            if (kb.f3Key.wasPressedThisFrame)
            {
                var ids = Generated.Teams.All.Select(t => t.Id).ToList();
                int i = TeamRoster.DevForce == null ? 0 : (ids.IndexOf(TeamRoster.DevForce) + 1) % (ids.Count + 1);
                TeamRoster.DevForce = i < ids.Count ? ids[i] : null;
                Plugin.Log.LogInfo($"Dev: next opponent forced to {TeamRoster.DevForce ?? "random"}");
            }
            if (kb.f4Key.wasPressedThisFrame && VersusState.Active && shift)
            {
                // a virtual gamepad for player 2 (tests the gamepad path and disconnects without hardware)
                if (testPad == null) testPad = InputSystem.AddDevice<Gamepad>("Overtime test pad");
                VersusState.Setup.Human(1).Input = new SlotInput { Kind = InputKind.Gamepad, DeviceId = testPad.deviceId, Label = "TEST GAMEPAD" };
                Plugin.Log.LogInfo($"Dev: P2 on a virtual gamepad (id {testPad.deviceId})");
            }
            else if (kb.f4Key.wasPressedThisFrame && VersusState.Active && kb.ctrlKey.isPressed && testPad != null)
            {
                if (testPad.added) { InputSystem.RemoveDevice(testPad); Plugin.Log.LogInfo("Dev: virtual gamepad unplugged"); }
                else { InputSystem.AddDevice(testPad); Plugin.Log.LogInfo("Dev: virtual gamepad plugged back in"); }
            }
            else if (kb.f4Key.wasPressedThisFrame && inMatch && VersusState.Active && Patches.VersusDriver.Current != null)
            {
                Patches.VersusDriver.Autopilot = !Patches.VersusDriver.Autopilot;
                Plugin.Log.LogInfo($"Dev: Versus autopilot {(Patches.VersusDriver.Autopilot ? "on" : "off")} (both players driven through the Versus input path)");
            }
            else if (kb.f4Key.wasPressedThisFrame && inMatch)
            {
                var server = FindObjectsByType<PlayerController>(FindObjectsSortMode.None)
                    .FirstOrDefault(p => p.attackDirection > 0f && !p.setter && p.name == "Player");
                if (server != null && !server.IsServing()) { server.StartServe(); Plugin.Log.LogInfo("Dev: player 1 serves"); }
            }
            if (kb.f5Key.wasPressedThisFrame && inMatch)
            {
                bool over = shift ? gm.OpponentGotPoint() : gm.PlayerGotPoint();
                Plugin.Log.LogInfo($"Dev: rally to {(shift ? "opponent" : "player")} -> {gm.playerPoints}-{gm.opponentPoints} over={over}");
            }
            if (kb.f6Key.wasPressedThisFrame && inMatch) gm.playerPoints = gm.matchLength;
            if (kb.f7Key.wasPressedThisFrame && inMatch) gm.opponentPoints = gm.matchLength;
            if (kb.f8Key.wasPressedThisFrame)
            {
                if (RunState.Mode == RunMode.Infinite) RunState.InfiniteMatch = 7; else GameManager.gameNumber = 7;
                Plugin.Log.LogInfo("Dev: next match is the final");
            }
            if (kb.f9Key.wasPressedThisFrame)
            {
                var s0 = Engine.Engine.Sides[0];
                Plugin.Log.LogInfo($"Dev state: mode={RunState.Mode} gameNumber={GameManager.gameNumber} infiniteMatch={RunState.InfiniteMatch} " +
                                   $"wins={RunState.InfiniteWins} loop={RunState.Loop} score={gm.playerPoints}-{gm.opponentPoints} touchStreak={s0.TouchStreak} " +
                                   $"rallyWins={s0.RallyWins} bank={s0.Bank:F2} timeScale={Time.timeScale:F2} " +
                                   $"perks=[{string.Join(", ", gm.playerStats.techniques.Select(t => t.name))}] " +
                                   $"opponent=[{string.Join(", ", Engine.Engine.StatsOf(1)?.techniques.Select(t => t.name) ?? new string[0])}] " +
                                   $"cards=[{string.Join(", ", StatCards.Points.Select(kv => kv.Key + " " + kv.Value))}]");
            }
            if (kb.f9Key.wasPressedThisFrame && Plugin.DumpScenes.Value) { Dumps.PlayerParts(); Dumps.BodySprites(); }
            if (kb.f10Key.wasPressedThisFrame)
            {
                foreach (var p in perks) if (!gm.playerStats.techniques.Contains(p)) gm.playerStats.techniques.Add(p);
                Plugin.Log.LogInfo("Dev: all Overtime perks given (active from the next match)");
            }
            if (kb.f11Key.wasPressedThisFrame)
            {
                OnlyOvertimeCards = !OnlyOvertimeCards;
                Plugin.Log.LogInfo($"Dev: perk screens show only Overtime perks = {OnlyOvertimeCards}");
            }
            if (kb.f12Key.wasPressedThisFrame && RunState.Mode == RunMode.Infinite)
            {
                RunState.InfiniteMatch += 8;
                Plugin.Log.LogInfo($"Dev: next infinite match {RunState.InfiniteMatch} (tier {(RunState.InfiniteMatch - 1) / 8})");
            }
        }
    }
}

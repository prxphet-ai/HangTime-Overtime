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
    //  F6 win this match   F7 lose this match   F8 next win leads to the final match   F9 log state (Ctrl+F9: force player 1 block 66/58/50/40/30, auto-blocking; Shift+F9: court colliders)
    //  Ctrl+1 launch player 1 over the net wall   Ctrl+2 throw the ball over a side wall   Ctrl+0 turn those two fixes off/on
    //  F10 give every Overtime perk (Ctrl+F10: player 2 jumps right after every serve, logs if it blocked)   F11 perk screens show only Overtime perks   F12 next infinite match one tier harder
    internal class DevKeys : MonoBehaviour
    {
        public static bool OnlyOvertimeCards;
        private static int lab = -1;
        private static Gamepad testPad;

        private static readonly float[] BlockTests = { 0f, 66f, 58f, 50f, 40f, 30f };
        private int blockTest;
        private float probeLog, blockedAt = -10f;
        private static readonly HarmonyLib.AccessTools.FieldRef<PlayerController, bool> blockingOf = HarmonyLib.AccessTools.FieldRefAccess<PlayerController, bool>("blocking");
        private static readonly HarmonyLib.AccessTools.FieldRef<PlayerController, BoxCollider2D> blockBoxOf = HarmonyLib.AccessTools.FieldRefAccess<PlayerController, BoxCollider2D>("blockHitBox");
        private static readonly HarmonyLib.AccessTools.FieldRef<PlayerController, Transform> jumpCheckOf = HarmonyLib.AccessTools.FieldRefAccess<PlayerController, Transform>("jumpCheckPosition");

        // development: player 1 block-jumps whenever the ball is on the other side, and its state is logged
        private void BlockProbe()
        {
            var p1 = FindObjectsByType<PlayerController>(FindObjectsSortMode.None).FirstOrDefault(Engine.Look.IsPlayerOne);
            var ball = FindFirstObjectByType<BallMovement>();
            if (p1 == null || ball == null) return;
            if (p1.getGrounded() && !blockingOf(p1) && ball.transform.position.x > 0f && ball.GetServeTimer() < -0.7f && Time.time - blockedAt > 1.5f)
            {
                blockedAt = Time.time;
                p1.UpInputPressed();
            }
            if (Time.time - probeLog > 0.5f && Time.time - blockedAt < 4f)
            {
                probeLog = Time.time;
                var box = blockBoxOf(p1);
                Plugin.Log.LogInfo($"[block] t+{Time.time - blockedAt:F1} y{p1.transform.position.y:F2} grounded {p1.getGrounded()} blocking {blockingOf(p1)} " +
                                   $"box {(box.enabled ? "on" : "off")} size {box.size.y:F2} offset {box.offset.y:F2} bottom {box.bounds.min.y:F2} feet {jumpCheckOf(p1).position.y:F2} vy {p1.GetComponent<Rigidbody2D>().linearVelocityY:F1}");
            }
        }

        private bool serveProbe;
        private float wallProbeUntil, wallMaxX = -99f, wallMaxY = -99f, ballProbeUntil;
        private bool ballThrowPending;

        // the wall probe: player 1 keeps holding toward the net through the jump (after the Versus driver's input)
        private void LateUpdate()
        {
            if (wallProbeUntil <= 0f) return;
            var p1 = FindObjectsByType<PlayerController>(FindObjectsSortMode.None).FirstOrDefault(Engine.Look.IsPlayerOne);
            p1?.SetXInput(1f);
        }
        private float serveJumpAt = -10f;

        // development (Ctrl+F10): the right spiker jumps as soon as the other side has served; logs whether it blocked
        private void ServeProbe()
        {
            var ball = FindFirstObjectByType<BallMovement>();
            var p2 = FindObjectsByType<PlayerController>(FindObjectsSortMode.None).FirstOrDefault(p => p.attackDirection == -1f && !p.setter);
            if (ball == null || p2 == null) return;
            float st = ball.GetServeTimer();
            if (ball.transform.position.x < 0f && st < 0f && st > -0.6f && p2.getGrounded() && Time.time - serveJumpAt > 1.5f)
            {
                serveJumpAt = Time.time;
                p2.UpInputPressed();
                Plugin.Log.LogInfo($"[serveprobe] P2 jumped {-st:F2}s after the serve: blocking {blockingOf(p2)}");
            }
        }

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
            else if (kb.f4Key.wasPressedThisFrame && inMatch && VersusState.Active && (Patches.VersusDriver.Current != null || Online.MatchSync.Mirroring))
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
            if (kb.f9Key.wasPressedThisFrame && kb.ctrlKey.isPressed)
            {
                blockTest = (blockTest + 1) % BlockTests.Length;
                foreach (var fx in Engine.ControllerFx.All) if (fx != null && Engine.Look.IsPlayerOne(fx.Pc)) fx.TestBlock = BlockTests[blockTest];
                Plugin.Log.LogInfo($"Dev: player 1 block forced to {BlockTests[blockTest]} (0 = normal); blocking every time the ball crosses");
            }
            if (blockTest > 0 && inMatch) BlockProbe();
            if (kb.f10Key.wasPressedThisFrame && kb.ctrlKey.isPressed) { serveProbe = !serveProbe; Plugin.Log.LogInfo($"Dev: serve-block probe {(serveProbe ? "on" : "off")}"); }
            if (serveProbe && inMatch) ServeProbe();
            // Ctrl+1: player 1 launched over the net's invisible wall; Ctrl+2: the ball thrown over a side wall; Ctrl+0: those fixes off/on
            if (kb.ctrlKey.isPressed && kb.digit0Key.wasPressedThisFrame) { Patches.BallPatches.Arena.Off = !Patches.BallPatches.Arena.Off; Plugin.Log.LogInfo($"Dev: arena fixes {(Patches.BallPatches.Arena.Off ? "OFF" : "on")}"); }
            if (kb.ctrlKey.isPressed && kb.digit1Key.wasPressedThisFrame && inMatch)
            {
                var p1 = FindObjectsByType<PlayerController>(FindObjectsSortMode.None).FirstOrDefault(Engine.Look.IsPlayerOne);
                if (p1 != null) { var rb = p1.GetComponent<Rigidbody2D>(); p1.transform.position = new Vector3(-3f, -7f, 0f); rb.position = new Vector2(-3f, -7f); rb.linearVelocity = new Vector2(12f, 160f); wallProbeUntil = Time.time + 3f; wallMaxX = -99f; Plugin.Log.LogInfo("Dev: player 1 launched at the net"); }
            }
            if (wallProbeUntil > 0f)
            {
                var p1 = FindObjectsByType<PlayerController>(FindObjectsSortMode.None).FirstOrDefault(Engine.Look.IsPlayerOne);
                if (p1 != null) { wallMaxX = Mathf.Max(wallMaxX, p1.transform.position.x); wallMaxY = Mathf.Max(wallMaxY, p1.transform.position.y); }
                if (Time.time > wallProbeUntil) { wallProbeUntil = 0f; Plugin.Log.LogInfo($"[wall] player 1 got to x {wallMaxX:F2} (net at 0), top y {wallMaxY:F1}, ended at x {p1?.transform.position.x:F2}"); wallMaxY = -99f; }
            }
            if (kb.ctrlKey.isPressed && kb.digit2Key.wasPressedThisFrame && inMatch)
            {
                var b = FindFirstObjectByType<BallMovement>();
                if (b != null) ballThrowPending = true;
            }
            if (ballThrowPending && inMatch)
            {
                var b = FindFirstObjectByType<BallMovement>();
                if (b != null && !BallMovement.rallyDone && b.GetServeTimer() < -0.5f)
                {
                    ballThrowPending = false;
                    var brb = b.GetComponent<Rigidbody2D>();
                    b.transform.position = new Vector3(60f, 80f, 0f); brb.position = new Vector2(60f, 80f); brb.linearVelocity = new Vector2(45f, 70f);
                    Plugin.Log.LogInfo($"Dev: ball thrown over the right wall in free flight (score {gm.playerPoints}-{gm.opponentPoints})");
                    ballProbeUntil = Time.time + 6f;
                }
            }
            if (ballProbeUntil > 0f && Time.time > ballProbeUntil)
            {
                ballProbeUntil = 0f;
                var b = FindFirstObjectByType<BallMovement>();
                Plugin.Log.LogInfo($"[ballprobe] 6 s later: ball at ({b?.transform.position.x:F0}, {b?.transform.position.y:F0}), score {gm.playerPoints}-{gm.opponentPoints}, camera size {Camera.main?.orthographicSize:F1}");
            }
            if (kb.f9Key.wasPressedThisFrame && !kb.ctrlKey.isPressed)
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
            if (kb.f9Key.wasPressedThisFrame && kb.altKey.isPressed)
            {
                // development: the game's own input actions and their bindings (Alt+F9)
                var im = GameManager.Instance.inputManager;
                foreach (var map in new[] { im.Gameplay, im.menus })
                    if (map != null)
                        foreach (var a in map.actions)
                            Plugin.Log.LogInfo($"[input] {map.name}/{a.name}: " + string.Join(" | ", a.bindings.Select(bd => (bd.isComposite ? "[" + bd.name + "]" : bd.isPartOfComposite ? bd.name + "=" : "") + bd.effectivePath)));
            }
            if (kb.f9Key.wasPressedThisFrame && shift)
            {
                // development: the court's colliders (floor, walls, the net and its invisible wall)
                foreach (var c in FindObjectsByType<Collider2D>(FindObjectsSortMode.None).Where(c => c.enabled && c.attachedRigidbody == null || (c.attachedRigidbody != null && c.attachedRigidbody.bodyType == RigidbodyType2D.Static)))
                    Plugin.Log.LogInfo($"[collider] {c.name} tag {c.tag} layer {LayerMask.LayerToName(c.gameObject.layer)} trigger {c.isTrigger} bounds x {c.bounds.min.x:F1}..{c.bounds.max.x:F1} y {c.bounds.min.y:F1}..{c.bounds.max.y:F1}");
                var b = FindFirstObjectByType<BallMovement>();
                if (b != null) Plugin.Log.LogInfo($"[collider] ball rb detection {b.GetComponent<Rigidbody2D>().collisionDetectionMode} radius {b.GetComponent<Collider2D>()?.bounds.extents.x:F2}");
            }
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

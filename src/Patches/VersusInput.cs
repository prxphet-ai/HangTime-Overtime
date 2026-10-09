using System.Linq;
using HangtimeOvertime.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HangtimeOvertime.Patches
{
    // One frame of a player's input, whatever the device (and later, a network peer).
    internal struct PadState
    {
        public float X;                                   // -1..1 movement
        public bool Up, Down;                             // jump/hit/spin serve, receive/float serve (this frame)
        public bool Left, Right;                          // menu steps (this frame)
        public bool Confirm, Back;                        // menu confirm / back (this frame)
        public bool Connected;
    }

    internal static class VersusInput
    {
        public static Gamepad PadOf(SlotInput b) =>
            b != null && b.Kind == InputKind.Gamepad ? Gamepad.all.FirstOrDefault(g => g.deviceId == b.DeviceId) : null;

        public static PadState Read(SlotInput b)
        {
            var s = new PadState();
            var kb = Keyboard.current;
            switch (b?.Kind ?? InputKind.None)
            {
                case InputKind.KeysWASD:
                case InputKind.KeysArrows:
                    if (kb == null) break;
                    bool wasd = b.Kind == InputKind.KeysWASD;
                    var l = wasd ? kb.aKey : kb.leftArrowKey;
                    var r = wasd ? kb.dKey : kb.rightArrowKey;
                    var u = wasd ? kb.wKey : kb.upArrowKey;
                    var d = wasd ? kb.sKey : kb.downArrowKey;
                    s.Connected = true;
                    s.X = (r.isPressed ? 1f : 0f) - (l.isPressed ? 1f : 0f);
                    s.Up = u.wasPressedThisFrame;
                    s.Down = d.wasPressedThisFrame;
                    s.Left = l.wasPressedThisFrame;
                    s.Right = r.wasPressedThisFrame;
                    s.Confirm = u.wasPressedThisFrame || (wasd ? kb.spaceKey.wasPressedThisFrame : kb.enterKey.wasPressedThisFrame);
                    s.Back = kb.escapeKey.wasPressedThisFrame;
                    break;
                case InputKind.Gamepad:
                    var g = PadOf(b);
                    if (g == null) break;
                    s.Connected = true;
                    float x = g.leftStick.ReadValue().x;
                    if (Mathf.Abs(x) < 0.25f) x = 0f;
                    if (g.dpad.left.isPressed) x = -1f;
                    if (g.dpad.right.isPressed) x = 1f;
                    s.X = Mathf.Clamp(x * 1.6f, -1f, 1f);
                    s.Up = g.buttonSouth.wasPressedThisFrame || g.rightTrigger.wasPressedThisFrame;
                    s.Down = g.buttonWest.wasPressedThisFrame || g.leftTrigger.wasPressedThisFrame;
                    s.Left = g.dpad.left.wasPressedThisFrame || g.leftStick.left.wasPressedThisFrame;
                    s.Right = g.dpad.right.wasPressedThisFrame || g.leftStick.right.wasPressedThisFrame;
                    s.Confirm = g.buttonSouth.wasPressedThisFrame || g.startButton.wasPressedThisFrame;
                    s.Back = g.buttonEast.wasPressedThisFrame;
                    break;
            }
            return s;
        }

        // "Press a button to join": the first device that something was pressed on this frame, or null.
        public static SlotInput DetectJoin()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
                    return new SlotInput { Kind = InputKind.KeysWASD, Label = "W A S D" };
                if (kb.upArrowKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
                    return new SlotInput { Kind = InputKind.KeysArrows, Label = "ARROW KEYS" };
            }
            foreach (var g in Gamepad.all)
                if (g.buttonSouth.wasPressedThisFrame || g.startButton.wasPressedThisFrame)
                    return new SlotInput { Kind = InputKind.Gamepad, DeviceId = g.deviceId, Label = "GAMEPAD " + (Gamepad.all.ToList().IndexOf(g) + 1) };
            return null;
        }

        // A gamepad's button press from a pad nobody uses (to take over a disconnected player's slot).
        public static SlotInput FreePadPressed(params SlotInput[] taken)
        {
            foreach (var g in Gamepad.all)
                if ((g.buttonSouth.wasPressedThisFrame || g.startButton.wasPressedThisFrame) && !taken.Any(t => t != null && t.Kind == InputKind.Gamepad && t.DeviceId == g.deviceId))
                    return new SlotInput { Kind = InputKind.Gamepad, DeviceId = g.deviceId, Label = "GAMEPAD " + (Gamepad.all.ToList().IndexOf(g) + 1) };
            return null;
        }
    }

    // Drives the human-controlled players of a Versus match from their own devices, through the same calls the game's
    // input and AI use (SetXInput / UpInputPressed / DownInputPressed). A slot whose device disconnects pauses the match.
    internal class VersusDriver : MonoBehaviour
    {
        public static VersusDriver Current;
        public PlayerController[] Players = new PlayerController[2];
        public bool Frozen;                               // between rounds: nobody moves
        public static bool Autopilot;                     // development (DevKeys F4): both players play themselves, same input path
        public int Disconnected = -1;                     // side whose device is gone, or -1
        private float savedTimeScale = 1f;

        private void Awake() => Current = this;
        private void OnDestroy() { if (Current == this) Current = null; if (Disconnected >= 0) Time.timeScale = savedTimeScale; }

        private void Update()
        {
            var setup = VersusState.Setup;
            if (setup == null) return;
            for (int side = 0; side < 2; side++)
            {
                var slot = setup.Human(side);
                if (slot == null || slot.Control != SlotControl.Human) continue;
                var s = Autopilot && Players[side] != null ? AutoPad(side, Players[side]) : VersusInput.Read(slot.Input);
                if (!s.Connected) { OnLost(side); continue; }
                if (Disconnected == side) OnBack(side);
                var pc = Players[side];
                if (pc == null || !pc.isActiveAndEnabled) continue;
                if (Frozen || Disconnected >= 0 || PauseController.paused || GameManager.gameOver) { pc.SetXInput(0f); continue; }
                pc.SetXInput(s.X);
                if (s.Up) pc.UpInputPressed();
                if (s.Down) pc.DownInputPressed();
            }
            if (Disconnected >= 0)
            {
                // any free gamepad can take the lost player's place
                var other = setup.Human(1 - Disconnected)?.Input;
                var free = VersusInput.FreePadPressed(other);
                if (free != null) { setup.Human(Disconnected).Input = free; Plugin.Log.LogInfo($"Versus: P{Disconnected + 1} now on {free.Label}"); }
            }
        }

        // ------------------------------------------------------------ development autopilot (DevKeys only)
        private BallMovement ball;
        private readonly float[] serveAt = new float[2], swingAt = new float[2];

        private PadState AutoPad(int side, PlayerController pc)
        {
            var s = new PadState { Connected = true };
            if (ball == null) ball = FindFirstObjectByType<BallMovement>();
            if (ball == null) return s;
            float dir = side == 0 ? 1f : -1f;                                  // towards the net
            float x = pc.transform.position.x, bx = ball.transform.position.x, by = ball.transform.position.y;
            var rb = ball.GetComponent<Rigidbody2D>();
            float vy = rb != null ? rb.linearVelocityY : 0f;
            if (pc.IsServing())
            {
                if (Mathf.Abs(x) < pc.COURT_SIZE + 0.3f) { s.X = -dir; serveAt[side] = 0f; return s; }
                if (serveAt[side] == 0f) { serveAt[side] = Time.time; s.Up = true; return s; }
                if (Time.time - serveAt[side] > 0.35f && Time.time - serveAt[side] < 0.4f) s.Up = true;
                return s;
            }
            bool mine = Mathf.Sign(bx) == Mathf.Sign(x);
            float target = mine ? bx - dir * 1.5f : -dir * 22f;                // stand just behind the ball, or go home
            float d = target - x;
            s.X = Mathf.Abs(d) < 0.8f ? 0f : Mathf.Sign(d);
            float above = by - pc.transform.position.y;
            bool ours = BallMovement.lastTouched == (int)Mathf.Sign(x);     // our side touched it last (a set): attack it
            if (mine && ours && pc.getGrounded() && Mathf.Abs(bx - x) < 4f && above > 7f && above < 16f && vy < 4f && Time.time - swingAt[side] > 1f)
            {
                s.Up = true;                                                   // jump to attack
                swingAt[side] = Time.time;
            }
            else if (!pc.getGrounded() && Time.time - swingAt[side] > 0.15f && Time.time - swingAt[side] < 0.6f &&
                     Vector2.Distance(ball.transform.position, pc.transform.position + Vector3.up * 3f) < 4.5f)
            {
                s.Up = true;                                                   // swing
                swingAt[side] -= 1f;
            }
            return s;
        }

        private void OnLost(int side)
        {
            if (Disconnected >= 0) return;
            Disconnected = side;
            savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            VersusOverlay.Notice($"P{side + 1}'S CONTROLLER DISCONNECTED\n<size=70%>RECONNECT IT, OR PRESS A ON ANOTHER GAMEPAD</size>");
            Plugin.Log.LogInfo($"Versus: P{side + 1} device lost, match paused");
        }

        private void OnBack(int side)
        {
            Disconnected = -1;
            Time.timeScale = savedTimeScale;
            VersusOverlay.Notice(null);
            Plugin.Log.LogInfo($"Versus: P{side + 1} device back, match resumed");
        }
    }
}

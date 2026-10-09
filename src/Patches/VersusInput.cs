using System.Collections.Generic;
using System.Linq;
using HangtimeOvertime.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

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
        // the keyboard sets: left, right, up (jump/hit), down (receive), and an extra confirm key for menus
        private static (KeyControl l, KeyControl r, KeyControl u, KeyControl d, KeyControl ok)? Keys(InputKind k, Keyboard kb)
        {
            switch (k)
            {
                case InputKind.KeysWASD: return (kb.aKey, kb.dKey, kb.wKey, kb.sKey, kb.spaceKey);
                case InputKind.KeysArrows: return (kb.leftArrowKey, kb.rightArrowKey, kb.upArrowKey, kb.downArrowKey, kb.enterKey);
                case InputKind.KeysIJKL: return (kb.jKey, kb.lKey, kb.iKey, kb.kKey, kb.hKey);
                case InputKind.KeysNumpad: return (kb.numpad4Key, kb.numpad6Key, kb.numpad8Key, kb.numpad5Key, kb.numpadEnterKey);
                default: return null;
            }
        }

        public static readonly (InputKind kind, string label)[] KeyboardSets =
        {
            (InputKind.KeysWASD, "W A S D"), (InputKind.KeysArrows, "ARROW KEYS"), (InputKind.KeysIJKL, "I J K L"), (InputKind.KeysNumpad, "NUMPAD 8 4 5 6"),
        };

        public static Gamepad PadOf(SlotInput b) =>
            b != null && b.Kind == InputKind.Gamepad ? Gamepad.all.FirstOrDefault(g => g.deviceId == b.DeviceId) : null;

        public static PadState Read(SlotInput b)
        {
            var s = new PadState();
            var kb = Keyboard.current;
            var kind = b?.Kind ?? InputKind.None;
            if (kind == InputKind.Gamepad)
            {
                var g = PadOf(b);
                if (g == null) return s;
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
                return s;
            }
            if (kb == null) return s;
            var keys = Keys(kind, kb);
            if (keys == null) return s;
            var (l, r, u, d, ok) = keys.Value;
            s.Connected = true;
            s.X = (r.isPressed ? 1f : 0f) - (l.isPressed ? 1f : 0f);
            s.Up = u.wasPressedThisFrame;
            s.Down = d.wasPressedThisFrame;
            s.Left = l.wasPressedThisFrame;
            s.Right = r.wasPressedThisFrame;
            s.Confirm = u.wasPressedThisFrame || ok.wasPressedThisFrame;
            s.Back = kb.escapeKey.wasPressedThisFrame;
            return s;
        }

        // "Press a button to join": the first device that something was pressed on this frame, or null.
        public static SlotInput DetectJoin()
        {
            var kb = Keyboard.current;
            if (kb != null)
                foreach (var (kind, label) in KeyboardSets)
                {
                    var k = Keys(kind, kb).Value;
                    if (k.l.wasPressedThisFrame || k.r.wasPressedThisFrame || k.u.wasPressedThisFrame || k.d.wasPressedThisFrame || k.ok.wasPressedThisFrame)
                        return new SlotInput { Kind = kind, Label = label };
                }
            foreach (var g in Gamepad.all)
                if (g.buttonSouth.wasPressedThisFrame || g.startButton.wasPressedThisFrame)
                    return new SlotInput { Kind = InputKind.Gamepad, DeviceId = g.deviceId, Label = "GAMEPAD " + (Gamepad.all.ToList().IndexOf(g) + 1) };
            return null;
        }

        // A gamepad's button press from a pad nobody uses (to take over a disconnected player's slot).
        public static SlotInput FreePadPressed(IEnumerable<SlotInput> taken)
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
        public static bool Autopilot;                     // development (DevKeys F4): every human slot plays itself, same input path
        public readonly Dictionary<VersusSlot, PlayerController> Players = new Dictionary<VersusSlot, PlayerController>();
        public bool Frozen;                               // between rounds: nobody moves
        public VersusSlot Disconnected;                   // slot whose device is gone, or null
        private float savedTimeScale = 1f;
        private GameObject controllerWarning;             // the game's co-op "connect two controllers" message

        private void Awake() => Current = this;
        private void OnDestroy() { if (Current == this) Current = null; if (Disconnected != null) Time.timeScale = savedTimeScale; }

        private void Start()
        {
            var lim = FindFirstObjectByType<LocalInputManager>();
            controllerWarning = lim != null ? HarmonyLib.Traverse.Create(lim).Field("controllerErrorMessage").GetValue<GameObject>() : null;
        }

        private void Update()
        {
            var setup = VersusState.Setup;
            if (setup == null) return;
            // Versus assigns every device itself; the game's co-op warning about controllers doesn't apply
            if (controllerWarning != null && controllerWarning.activeSelf) controllerWarning.SetActive(false);
            foreach (var slot in setup.AllHumans)
            {
                Players.TryGetValue(slot, out var pc);
                var s = Autopilot && pc != null ? AutoPad(pc) : VersusInput.Read(slot.Input);
                if (!s.Connected) { OnLost(slot); continue; }
                if (Disconnected == slot) OnBack(slot);
                if (pc == null || !pc.isActiveAndEnabled) continue;
                if (Frozen || Disconnected != null || PauseController.paused || GameManager.gameOver) { pc.SetXInput(0f); continue; }
                pc.SetXInput(s.X);
                if (s.Up) pc.UpInputPressed();
                if (s.Down) pc.DownInputPressed();
            }
            if (Disconnected != null)
            {
                // any free gamepad can take the lost player's place
                var free = VersusInput.FreePadPressed(setup.AllHumans.Where(h => h != Disconnected).Select(h => h.Input));
                if (free != null) { Disconnected.Input = free; Plugin.Log.LogInfo($"Versus: P{Disconnected.Number} now on {free.Label}"); }
            }
        }

        // ------------------------------------------------------------ development autopilot (DevKeys only)
        private BallMovement ball;
        private readonly Dictionary<PlayerController, (float serveAt, float swingAt)> auto = new Dictionary<PlayerController, (float, float)>();

        private PadState AutoPad(PlayerController pc)
        {
            var s = new PadState { Connected = true };
            if (ball == null) ball = FindFirstObjectByType<BallMovement>();
            if (ball == null) return s;
            auto.TryGetValue(pc, out var t);
            float x = pc.transform.position.x, bx = ball.transform.position.x, by = ball.transform.position.y;
            float dir = x < 0f ? 1f : -1f;                                   // towards the net
            var rb = ball.GetComponent<Rigidbody2D>();
            float vy = rb != null ? rb.linearVelocityY : 0f;
            if (pc.IsServing())
            {
                if (Mathf.Abs(x) < pc.COURT_SIZE + 0.3f) { s.X = -dir; t.serveAt = 0f; auto[pc] = t; return s; }
                if (t.serveAt == 0f) { t.serveAt = Time.time; s.Up = true; auto[pc] = t; return s; }
                if (Time.time - t.serveAt > 0.35f && Time.time - t.serveAt < 0.4f) s.Up = true;
                return s;
            }
            // where the ball comes down to head height (gravity only), so a pair without a setter can receive serves
            if (rb != null && rb.gravityScale > 0f)
            {
                float g = Physics2D.gravity.y * rb.gravityScale, h = by - (pc.transform.position.y + 3f);
                float disc = vy * vy - 2f * g * h;
                if (disc > 0f) bx = Mathf.Clamp(bx + rb.linearVelocityX * ((-vy - Mathf.Sqrt(disc)) / g), -48f, 48f);
            }
            bool mine = Mathf.Sign(bx) == Mathf.Sign(x);
            // two humans on a side share it: the one nearer the ball goes for it, the other covers the back
            bool closest = !Players.Values.Any(o => o != null && o != pc && o.isActiveAndEnabled && Mathf.Sign(o.transform.position.x) == Mathf.Sign(x) &&
                                                     Mathf.Abs(o.transform.position.x - bx) < Mathf.Abs(x - bx));
            float target = mine && closest ? bx - dir * 1.5f : -dir * (closest ? 22f : 30f);
            float d = target - x;
            s.X = Mathf.Abs(d) < 0.8f ? 0f : Mathf.Sign(d);
            float above = by - pc.transform.position.y;
            bool ours = BallMovement.lastTouched == (int)Mathf.Sign(x);     // our side touched it last (a set): attack it
            if (mine && closest && ours && pc.getGrounded() && Mathf.Abs(bx - x) < 4f && above > 7f && above < 16f && vy < 4f && Time.time - t.swingAt > 1f)
            {
                s.Up = true;                                                   // jump to attack
                t.swingAt = Time.time;
            }
            else if (!pc.getGrounded() && Time.time - t.swingAt > 0.15f && Time.time - t.swingAt < 0.6f &&
                     Vector2.Distance(ball.transform.position, pc.transform.position + Vector3.up * 3f) < 4.5f)
            {
                s.Up = true;                                                   // swing
                t.swingAt -= 1f;
            }
            auto[pc] = t;
            return s;
        }

        private void OnLost(VersusSlot slot)
        {
            if (Disconnected != null) return;
            Disconnected = slot;
            savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            VersusOverlay.Notice($"P{slot.Number}'S CONTROLLER DISCONNECTED\n<size=70%>RECONNECT IT, OR PRESS A ON ANOTHER GAMEPAD</size>");
            Plugin.Log.LogInfo($"Versus: P{slot.Number} device lost, match paused");
        }

        private void OnBack(VersusSlot slot)
        {
            Disconnected = null;
            Time.timeScale = savedTimeScale;
            VersusOverlay.Notice(null);
            Plugin.Log.LogInfo($"Versus: P{slot.Number} device back, match resumed");
        }
    }
}

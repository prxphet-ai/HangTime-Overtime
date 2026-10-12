using HangtimeOvertime.Patches;
using TMPro;
using UnityEngine;

namespace HangtimeOvertime.Online
{
    // Both players see the connection's delay in the top-right corner while an online session is up (menus and matches):
    // the round trip in milliseconds, green / yellow / red.
    internal class PingHud : OvertimePanel
    {
        private static PingHud current;
        private TextMeshPro text;
        private float nextUpdate;

        // NetRunner: one per scene (it hangs on the scene's camera), only while connected
        public static void Tick()
        {
            if (!Net.Connected) { if (current != null) Destroy(current.gameObject); current = null; return; }
            if (current != null || Camera.main == null) return;
            var go = new GameObject("Overtime Ping");
            var hud = go.AddComponent<PingHud>();
            if (!hud.InitKit(320)) { Destroy(go); return; }
            hud.text = hud.Text("", new Vector2(hud.W / 2f - 6f, hud.H / 2f - 1.2f), 0.55f, 11f, Color.white, TextAlignmentOptions.Right);
            current = hud;
        }

        private void Update()
        {
            if (text == null || Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + 0.5f;
            int ms = Mathf.RoundToInt(Net.PingMs);
            text.color = ms < 80 ? new Color(0.55f, 1f, 0.55f) : ms < 150 ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.45f, 0.4f);
            text.text = $"PING {ms} MS  <size=70%>{Net.PeerName.ToUpperInvariant()}</size>";
        }

        private void OnDestroy() { if (current == this) current = null; }
    }
}

using System.Linq;
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
            bool show = Net.IsHost ? Net.HasFriends : Net.Connected;
            if (!show) { if (current != null) Destroy(current.gameObject); current = null; return; }
            if (current != null || Camera.main == null) return;
            var go = new GameObject("Overtime Ping");
            var hud = go.AddComponent<PingHud>();
            if (!hud.InitKit(320)) { Destroy(go); return; }
            hud.text = hud.Text("", new Vector2(hud.W / 2f - 12f, hud.H / 2f - 1.2f), 0.55f, 23f, Color.white, TextAlignmentOptions.Right);
            current = hud;
        }

        private void Update()
        {
            if (text == null || Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + 0.5f;
            string Col(int ms) => ms < 80 ? "#8CFF8C" : ms < 150 ? "#FFD95A" : "#FF7366";
            if (Net.IsClient)
            {
                int ms = Mathf.RoundToInt(Net.PingMs);
                text.text = $"<color={Col(ms)}>PING {ms} MS</color>";
            }
            else text.text = string.Join("   ", Net.Peers.Where(p => p.Welcomed).Select(p =>
                $"<size=70%>{p.Name.Replace("<", "‹").ToUpperInvariant()}</size> <color={Col(Mathf.RoundToInt(p.PingMs))}>{Mathf.RoundToInt(p.PingMs)} MS</color>"));
            text.color = Color.white;
        }

        protected override void OnDestroy() { base.OnDestroy(); if (current == this) current = null; }
    }
}

using System;
using HangtimeOvertime.Patches;
using UnityEngine;

namespace HangtimeOvertime.Debugging
{
    // development (DevKeys + -ovt-colortest): swatches of known colours, painted the way the menus paint their boxes, on an
    // opaque black card on the title screen; a screenshot shows how the game's rendering maps menu colours
    internal class ColorTest : OvertimePanel
    {
        private static bool shown;
        public static readonly float[] Levels = { 0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f };

        public static void Tick()
        {
            if (shown || !Plugin.DevKeys.Value || Array.IndexOf(Environment.GetCommandLineArgs(), "-ovt-colortest") < 0) return;
            if (!TitleController.onTitle || Camera.main == null) return;
            shown = true;
            var go = new GameObject("Overtime Color Test");
            var t = go.AddComponent<ColorTest>();
            if (!t.InitKit(900)) { Destroy(go); return; }
            t.Build();
        }

        private void Build()
        {
            Box("Card", Vector2.zero, new Vector2(W * 0.9f, 12f), Color.black, 0);
            float x0 = -W * 0.4f, step = W * 0.8f / (Levels.Length - 1);
            for (int i = 0; i < Levels.Length; i++)
            {
                float v = Levels[i];
                Box("Grey", new Vector2(x0 + i * step, 3.5f), new Vector2(step * 0.8f, 3f), new Color(v, v, v, 1f), 2);
                Box("Red", new Vector2(x0 + i * step, 0f), new Vector2(step * 0.8f, 3f), new Color(v, 0f, 0f, 1f), 2);
                Box("Blue", new Vector2(x0 + i * step, -3.5f), new Vector2(step * 0.8f, 3f), new Color(0f, 0f, v, 1f), 2);
            }
            Plugin.Log.LogInfo("Color test shown");
        }
    }
}

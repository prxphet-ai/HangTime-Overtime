using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HangtimeOvertime.Core;
using HangtimeOvertime.Patches;
using HarmonyLib;

namespace HangtimeOvertime
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "melty.hangtime.overtime";
        public const string Name = "Hangtime! Overtime";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> DumpScenes;
        internal static ConfigEntry<bool> DevKeys;
        internal static ConfigEntry<int> InfiniteRoundOffset;

        // Perk/mode trigger log, only with DumpScenes on (development).
        internal static void Trace(string msg) { if (DumpScenes != null && DumpScenes.Value) Log.LogInfo("[trace] " + msg); }

        private void Awake()
        {
            Log = Logger;
            DumpScenes = Config.Bind("Debug", "DumpScenes", false, "Log scene hierarchies and game data (for development).");
            DevKeys = Config.Bind("Debug", "DevKeys", false, "Test shortcuts F6-F12 (for development).");
            InfiniteRoundOffset = Config.Bind("Infinite", "Difficulty", 0,
                new ConfigDescription("Opponents in Infinite and Loops play as if it were this many rounds later (+) or earlier (-). " +
                                      "0 = as tuned. Try +2 if round 1 feels too easy.", new AcceptableValueRange<int>(-4, 10)));

            PerkRegistry.Build();
            Engine.Vfx.Init();
            RunState.LoadSave();
            RunPatches.Init();
            MenuPatches.Init();
            if (DumpScenes.Value) Debugging.SceneDumper.Hook();
            if (DevKeys.Value) gameObject.AddComponent<Debugging.DevKeys>();
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }
    }
}

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
        public const string Version = "0.3.1";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> DumpScenes;
        internal static ConfigEntry<bool> DevKeys;
        internal static ConfigEntry<int> InfiniteRoundOffset;
        internal static ConfigEntry<int> OnlinePort, OnlineBuffer;
        internal static ConfigEntry<string> OnlineJoin, OnlineBind;
        internal static ConfigEntry<float> OnlineTimeout;
        internal static ConfigEntry<bool> SkipTutorial, CheckForUpdates;
        internal static ConfigEntry<bool> ChatBubbles, ChatMuteOthers;

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

            OnlinePort = Config.Bind("Online", "Port", 27515, "Direct IP: the UDP port a host listens on (forward it on your router for friends outside your network).");
            OnlineJoin = Config.Bind("Online", "JoinAddress", "127.0.0.1", "Direct IP: the host JOIN BY IP connects to (address or address:port). Steam invites need no address.");
            OnlineBind = Config.Bind("Online", "ListenAddress", "0.0.0.0", "Direct IP: the local address a host listens on (0.0.0.0 = every network).");
            OnlineBuffer = Config.Bind("Online", "BufferMs", 70, new ConfigDescription("Online client: how far behind the host the picture runs, so it stays smooth (raise it on a shaky connection).", new AcceptableValueRange<int>(30, 400)));
            if (OnlineBuffer.Value == 90) OnlineBuffer.Value = 70;   // 0.2.5's first default
            OnlineTimeout = Config.Bind("Online", "TimeoutSeconds", 8f, "Online: seconds without hearing from the other player before the session ends.");

            SkipTutorial = Config.Bind("General", "SkipTutorial", false, "Never start the game's tutorial (the pause menu's TUTORIAL still plays it). Also in the title screen's SETTINGS.");
            CheckForUpdates = Config.Bind("General", "CheckForUpdates", true, "On launch, ask GitHub whether a newer Overtime release exists and say so on the title screen (nothing about you is sent). Also in SETTINGS.");
            ChatBubbles = Config.Bind("Chat", "Bubbles", true, "In matches, chat lines and emotes show as a bubble above the player (the chat log in the corner always shows them). Also in SETTINGS.");
            ChatMuteOthers = Config.Bind("Chat", "MuteOthers", false, "Online: other PCs' chat lines and emotes are not shown. Also in SETTINGS.");
            Patches.TitleBar.Init();
            Patches.SettingsMenu.Init();
            Core.LobbyRules.LoadLast();

            PerkRegistry.Build();
            Engine.Vfx.Init();
            RunState.LoadSave();
            RunSaves.Load();
            Engine.Look.Load();
            LookPatches.Init();
            VersusPatches.Init();
            GameManager.OnPlayerLose += RunSaves.ClearCurrent;   // a lost match ends the run (the lose screen is not a scene)
            RunPatches.Init();
            StreakPatches.Init();
            MenuPatches.Init();
            if (DumpScenes.Value) Debugging.SceneDumper.Hook();
            if (DevKeys.Value) { gameObject.AddComponent<Debugging.DevKeys>(); Debugging.DevOnline.Hook(gameObject); Debugging.DevPad.Hook(gameObject); }
            Patches.ControlsApply.Init();
            gameObject.AddComponent<Online.NetRunner>();
            Online.OnlineFlow.Init();
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        // the saved run is already written at every safe point; these make sure the file is on disk when the game goes away
        private void OnApplicationQuit() => RunSaves.Flush();
        private void OnApplicationFocus(bool focused) { if (!focused) RunSaves.Flush(); }
    }
}

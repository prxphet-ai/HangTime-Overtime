# Hangtime! Overtime — mod log

## Facts
- Game: Hangtime! (Steam 3861120, build 20865211), Leo Langlois. Unity 6000.0.34f1, Mono, 2D, URP.
- Install: `C:\Program Files (x86)\Steam\steamapps\common\Hangtime!`; saves `%USERPROFILE%\AppData\LocalLow\LeoLanglois\Hangtime!\save.json`.
- Anti-cheat: none. Melty: Hangtime! not in Melty's catalog (game_info custom-hangtime) → listing can only be a draft.
- Loader: BepInEx 5.4.23.5 win x64 (GitHub release, sha256 82f98785…32c4). Works on this Unity 6 Mono build.
- Decompiled code (not in repo): `..\hangtime-decomp` (ilspycmd 9.1).
- Save backup before modded launch: `~\.universal-modder\backups\hangtime-saves\20261008-032530.zip`.

## Route
BepInEx 5 plugin + HarmonyX, net472. Perks are real `Technique` ScriptableObjects (the game calls
OnBump/OnSpike/OnServe/OnTip itself); modes patch GameManager/UpgradeManager/TitleCard and use
`SceneManager.sceneLoaded` to set `GameManager.gameNumber` before any `Start` reads it.

## Game code notes
- Run = gameNumber 0 practice, 1-8 bracket; 3 mid boss 1, 6 mid boss 2, 7 combo team, 8 final boss (EndGame "Win").
- Upgrade screen: technique vs stat cards from gameNumber parity around `limitBreakLevel`; picks with
  `Random.Range(0, techniques.Capacity)` → after adding cards set `Capacity = Count`. The no-duplicate
  `while` loop never ends if < 2 unowned techniques remain.
- Opponent stats: `OpponentTeam.Init(gameNumber*3+1+bonus)` random level-ups, capped at level 3 → later
  tiers scale the team's own `StatUpgrade.levels` values instead.
- `StatUpgrade.Upgrade` to level 4 sets global LimitBreakEffects flags — never level opponents past 3.
- Player side is x < 0, attackDirection +1. `CameraController.Update` resets timeScale to 1 each frame.
- Title menu (Game scene): Title/…/Button group (ButtonGroup) → "1 Player Button" (TitleButton, 1P),
  "2 Player button", "QUIT" (QuitButton); spacing 3.3 world units.
- Window capture: ffmpeg gfxcapture `window_title=Hangtime` (window_exe with "!" never matches).
- Input (1P): `LocalInputManager` sends MoveP1/UpP1/DownP1 (and MoveP3/UpP3/DownP3) to player 1 from every keyboard set
  and gamepad; co-op uses P2 actions + `ManualInputRouter` (arrows vs WASD). Every player, human or AI, is driven only
  through `PlayerController.SetXInput / UpInputPressed / DownInputPressed`.
- AI: right team = `SpikerInput` (also sets `BallMovement.opponent`, the right side's server, in its Start) + `SetterInput`
  (chases the ball on its side and sets). Left bot setter is `MultiplayerController.botSetter` in 1P. Nothing else
  references `SpikerInput`, so a human can replace it.
- Serve: `UpInputPressed` while serving needs |x| > COURT_SIZE (step back), jump, then Up (spin) / Down (float) in the
  air once the "Serve Jump" animation plays.
- Match end: `GameManager.Update` -> `EndGame(nextScene)` (IEnumerator, "Upgrade"/"Win"/"Lose"); a loss is not a scene
  (event `GameManager.OnPlayerLose` + in-scene lose menu).
- TMP labels copied from the title buttons carry negative side margins and auto-size their container: set `margin = 0`,
  `autoSizeTextContainer = false` and `textWrappingMode` (Unity 6 TMP) before relying on wrapping.
- The player's own hair sprite (`Hair_0`) sits in a 2000x2000 atlas "Hair" at rect (730,1216,292,235).

## Build
`python tools/sheets.py gen` (preflight + generate) → `dotnet build src -c Release -p:Deploy=true`.

## v0.2 overhaul (2026-10-08)
- User rules: no perk may change the score. Removed All In / Glass Cannon (now stat cards), Showboat (tip -> stored spike power),
  Second Wind (match point against -> speed/jump burst). PointPatches deleted; hooks sheet has changes_score = no on every row.
- Data-driven perk engine (src/Engine): perks.json rows = triggers + conds + fx list; effects.json = catalog; both teams.
- 61 perks (51 new, 3 reworked, 7 kept), 22 stat cards (stats.json units; Recovery & Set are mod stats), 8 new teams.
- Team prefabs: regular Rensho, Hinami Kai, Daigan Tech, Namasito Academy, Kozuki Dan; combo Aomori, Ten-Roku; boss Tenzio,
  Sunaumi High, Shirogane. Characters are modular sprites: Core_0 = jersey, Thigh_0* = shorts, Hair_0/Front hair.
- Gym banner = cloth sprite + 'Opponent Banner' emblem child (OpponentBanner.mySprite); new teams get a color disc + TMP name.
- UpgradeManager.indexNum is static and wraps with upgrades.Capacity: reset it when the stat pool size changes.
- Player stat level tables: Spike 1.05/1.40/1.70/2.10 (LB 3.0), Jump 61/66/69/74, Block 73/79/81/84, Bump 2.75/4/7/9.

## Status / next
- Preflight clean (45 hooks, 61 perks, 22 cards, 8 teams), build OK, all 61 perks fired live via dev lab (F1/F2) without errors.
- Next: watch triggers in normal play (dig, block, setter_set, streak, enemy_streak, match_point_against), stat cards,
  loop button, tier scaling; then switch DevKeys/DumpScenes off for release.

## Versus (1v1, 1v2, 2v2)
- Title VERSUS -> join screen (mode tabs, per-device join, team pick by each side's first player, ready) -> rounds to 5 points, first to 5 rounds; the loser of
  each round picks one of 3 upgrades. Code: `src/Core/Versus.cs` (setup slots, state, offers),
  `src/Patches/VersusInput.cs` (devices, driver, disconnects), `src/Patches/VersusPatches.cs` (round-end hook, match
  setup), `src/Patches/VersusUI.cs` (overlays, round flow), `src/Patches/VersusMenu.cs` (join screen).
  Rules: `sheets/modes.json` -> `versus`.
- Online (`src/Online/`): `Links.cs` (direct-IP UDP with a reliable channel; Steam lobby + SteamNetworkingMessages),
  `Net.cs` (session, handshake/version check, pings/timeouts, the client's devices as remote pads, Steam lobby calls),
  `MatchSync.cs` (host snapshots + timed events; the client's mirror and the record/hold-back hooks), `OnlineFlow.cs`
  (join screen, round loads, between-round screens, notices, session end). Config: `[Online]` in the BepInEx config.
  Dev harness: `src/Debug/DevOnline.cs` (`-ovt-host` / `-ovt-join` / `-ovt-auto` ..., DevKeys on).

## Feature batch (2026-10-20)
- Bugs: `ControllerPatches.OwnSide` (players stay on their side above the net's invisible wall, top y 19.3);
  `BallPatches.Arena` (a ball that leaves the arena ends the rally as out; it used to fall forever past x 100 and pin the
  camera zoomed in). Court: floor x -100..100 top -10, side walls |x| 70..80 to y 88.
- Title bar + settings (`src/Patches/TitleBar.cs`): top-left buttons on the title screen; SETTINGS has toggles stored in
  the BepInEx config. Tutorial skip = `[General] SkipTutorial` (GameManager.Awake postfix).
- Update checker (`src/Patches/UpdateCheck.cs`): GitHub releases/latest on title load, notice in the title bar; `[General]
  CheckForUpdates`. Needs `UnityEngine.UnityWebRequestModule` (referenced in the csproj).
- Report bug (`src/Patches/BugReport.cs`): preview -> GitHub new-issue link or clipboard; scrubbed of names/paths/IPs.
  Pause menu button = clone of the pause menu's lowest button (PauseController.Start postfix). Needs
  `UnityEngine.IMGUIModule` (clipboard).
- Win streaks: `src/Core/Records.cs` (HangtimeOvertime_stats.json) + `src/Patches/StreakPatches.cs` (EndGame prefix,
  lose banner, upgrade-card line, Versus via VersusFlow / the online Screen message). Online protocol 4.
- Controls: `src/Core/Controls.cs` (HangtimeOvertime_controls.json), `src/Patches/ControlsMenu.cs` (CONTROLS screen),
  `src/Patches/ControlsApply.cs` (game action overrides at InputManager.Start; co-op split keys). Game actions: MoveP1/UpP1/
  DownP1 = arrows + pad, MoveP3/UpP3/DownP3 = WASD, P2 = WASD + pad (co-op). Dev virtual gamepad: `src/Debug/DevPad.cs`.
- Gamepad menus: `src/Patches/PanelNav.cs` (stops, focus frame, pad prompts, `OnScreenKeyboard`). Game gotcha:
  `ButtonGroup.OnEnable` subscribes to A from a coroutine one frame later, even if the group was disabled meanwhile ->
  `held_group_guard` (Select prefix: only while enabled).
- Collection: `src/Core/Collection.cs` (HangtimeOvertime_collection.json: keys team:/perk:/vperk:/card:/vcard: with seen,
  picked, beaten; catalog of the game's own card titles), `src/Patches/CollectionPatches.cs` (TechniqueButton / UpgradeButton
  Start + Click, EndGame), `src/Patches/CollectionMenu.cs`. `Scaling.VanillaTechniques` is generated from scaling.json.
- Bracket: `src/Core/Bracket.cs` (model + Classic from `GameManager.tournamentLineUps` / midBoss1 / midBoss2 / finalBoss;
  combo team = random at kick-off), `src/Patches/BracketPanel.cs` (UpgradeManager.Start, WinManager.Start). Game facts:
  `UpgradeManager.LoadGame` does `gameNumber++`; `TitleCard.MatchTitles[gameNumber]` are the round names.
- Panel kit (`OvertimePanel.InitKit`): fixed layout height 36 (panel scaled by view/36), the title label's font kept for
  other scenes. Upgrade scene camera view 32, win scene 17.
- Daily runs: `src/Core/Daily.cs` (seed, Random push/pop, offers, HangtimeOvertime_daily.json), `src/Patches/DailyPatches.cs`,
  `src/Patches/DailyMenu.cs`; `RunState.Daily` / `DailyCounts`; run slot `daily`; `TeamRoster.DailyPick`. Game facts: the
  upgrade cards are drawn in the `UpgradeManager.CreateUpgrade` coroutine (Random across yields), button `Start` runs later.
- Daily cup: `RunState.StartCup/EndCup`, `Daily.Cup` (Normal mode + Daily), run slot `cup`, `TeamRoster.CupPick/CupName`;
  DAILY RUN menu RUN / CUP tabs.
- Draft: `sheets/draft.json` (settings, power rows, simulator-checked packages) -> `Generated.Draft`; `tools/draft.py`
  power / build / check; `src/Core/Drafting.cs` (deal, opponent perks), `src/Patches/DraftMenu.cs`; run slot `draft`;
  `RunState.Draft`; Versus DRAFT START (`VersusOverlay.ShowDraft`, `VersusOffer.Draft`). Dev: `-ovt-draft` (host).
- Individual picks: `src/Engine/Ownership.cs` (checked in `Engine.FirePerk`), `src/Patches/CoopPick.cs`. Co-op characters
  = `LocalInputManager.player1` / `player2`; Versus = `VersusDriver.Current.Players`. F9 logs ownership.
- Chat: `sheets/chat.json` -> `Generated.Chat`; `src/Patches/ChatHud.cs` (HUD spawned from NetRunner per Game scene;
  PlayerController SetXInput / UpInputPressed / DownInputPressed and PauseController.Pause_Unpause prefixes); online
  `Msg.MatchChat` 73, protocol 5. Config `[Chat] Bubbles / MuteOthers`. Dev: `-ovt-chat`.
- Lobby rules: `sheets/rules.json` -> `Generated.RulePresets`; `src/Core/LobbyRules.cs` (RuleSet, presets, custom slots,
  wire format, starting perks), `src/Patches/RulesPanel.cs` (+ BanPanel); `Msg.Menu` and `Msg.Load` carry the rules
  (protocol 6). Dev: `-ovt-rules <preset id>` (host).
- Replays: `src/Online/Replays.cs` (recorder, files, ReplayPlayer, ReplayHud), `src/Patches/ReplayMenu.cs`. `MatchSync.Emit`
  sends to friends and / or the replay; `Mirror.ReplayMode` + `ReplayTime`; `NetReader.Raw`. Runs start `HostSync` at
  kick-off when recording. Config `[Replays] Record`.
- Party cup: `src/Core/Party.cs` (bracket, AI fill, forfeits), `src/Patches/PartyMenu.cs` (+ PartyFlow). Versus: an AI
  right side keeps the game's SpikerInput; `VersusSetup.Names`; `Msg.PartyBracket` 74, protocol 7. Dev: `-ovt-party`.
- Spectators: `src/Online/Spectator.cs` (SpectatorHud, CameraModes); `Peer.Spectator`, `Net.ImSpectator`, `Msg.Spectate`
  75, protocol 8; a join during a match becomes a spectator; `AllReady` skips spectators. Dev: `-ovt-watchdemo`.
- Co-op Infinite: local via `ContinueMenu.ShowCoop` (title CO-OP), run slot `coop` (`players`, `owners`); online as
  Versus format `coop` (`VersusState.Coop`, `CoopSlot`, `PickCoopOpponent`; `VersusPatches.SetupMatch` scales instead of
  equal footing; `VersusOverlay.CoopScreen`). `RoundScreen.RematchLabel`; protocol 9. Dev: `-ovt-winfirst N` (host).
- Leaderboards: `server/` (Cloudflare Worker `worker.js` + D1 `schema.sql`, `dev_server.mjs` runs it on node:sqlite,
  `SETUP.md`); `sheets/community.json` -> `Generated.Community` + `server/generated.js`. Client: `src/Online/Community.cs`
  (Identity = local secret, id = sha256[:20]; Get / Post with UnityWebRequest), `src/Online/Leaderboards.cs` (pending
  queue, Finish / Flush), `src/Core/RunClock.cs` (unscaled time, run log, taint; saved in the run slot),
  `src/Patches/LeaderboardMenu.cs` (+ Toast). Hooks: Classic finish in `StreakPatches` EndGame prefix (nextScene "Win"),
  Infinite in `RunState.EndInfinite`. Config `[Community] Server` (empty = offline) / `Submit`. Dev: `-ovt-boardtest`
  (dev keys still submit), `-ovt-profile NAME` (second identity file).

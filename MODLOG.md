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

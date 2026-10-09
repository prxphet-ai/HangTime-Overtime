# Hangtime! Overtime

A gameplay mod for **[Hangtime!](https://store.steampowered.com/search/?term=Hangtime)**, the anime volleyball game. It adds an endless mode,
dozens of new abilities and teams, a character creator and run saves — all built on BepInEx.

*Made by **averageprxphet**. Unofficial fan mod, not affiliated with the developers of Hangtime!*

## Features

- **Infinite mode** — an endless run from the title screen. Opponents are picked at random and get stronger every round
  (better stats, then their own abilities), with a difficulty setting if you want it harder.
- **Loops** — beat the Classic final and keep going with LOOP 2, 3, ...
- **144 new abilities** — elemental and supernatural style perks with their own visuals: blazing spikes, freezing blocks, shadow clones, lightning chains and more.
- **47 stat cards** — boosts, playstyle cards and big-risk trade-offs.
- **34 new teams** — each with its own colours, hand-drawn banner emblem, voice lines, play style and signature abilities.
  Opponents announce their abilities at the start of a match.
- **Versus (1v1, 1v2, 2v2)** — up to four players on one PC (WASD, arrow keys, IJKL, numpad or gamepads). 1v1: each
  player has an AI setter. 1v2: one player and an AI setter against two players. 2v2: two players a side. Rounds to 5
  points, first to 5 rounds; the losing side picks a perk or stat card each round (a pair takes turns), so builds get
  wilder as the match goes on.
- **Run saves** — close the game mid-run (Infinite or Classic) and pick up where you left off with CONTINUE on the title screen.
- **Character creator** — hair style and colour, skin tone, jersey and shorts colours, number, build, headband, wristbands,
  knee pads and shoes. Cosmetic only.

The full list of every team, ability and stat card is in [CATALOG.md](CATALOG.md).

## Install

1. Install **[BepInEx 5](https://github.com/BepInEx/BepInEx/releases)** (x64, version 5.4.x) into your Hangtime! folder
   (Steam: right-click Hangtime! → Manage → Browse local files). Run the game once and close it.
2. Download the mod from the [Releases](../../releases) page and unzip it into `Hangtime!/BepInEx/plugins/`, so you have
   `BepInEx/plugins/HangtimeOvertime/HangtimeOvertime.dll` (plus its `emblems` and `title` folders).
3. Start the game. The title screen should read **HangTime! OVERTIME**.

To uninstall, delete the `BepInEx/plugins/HangtimeOvertime` folder.

### Settings

`BepInEx/config/melty.hangtime.overtime.cfg` (created on first launch):

| Setting | Default | What it does |
|---|---|---|
| `[Infinite] Difficulty` | `0` | Opponents play as if it were this many rounds later (`-4` to `10`). Try `2` if the start feels easy. |
| `[Debug] DevKeys` | `false` | Test shortcuts on F1–F12 (for development). |
| `[Debug] DumpScenes` | `false` | Detailed logging (for development). |

Saves (runs and your character's look) live next to the game's own save in
`%USERPROFILE%\AppData\LocalLow\LeoLanglois\Hangtime!\`. The mod never writes the game's own `save.json`.

## Building from source

Needs the **.NET SDK** (any recent version), **Python 3** with `dnfile` and `Pillow`, and Hangtime! with BepInEx installed.

```bash
pip install dnfile pillow
python tools/build.py --deploy
```

`build.py` checks the data sheets, generates code from them, builds the plugin and (with `--deploy`) copies it into the
game. If the game isn't in the default Steam folder, pass `-p:GameDir="D:\path\to\Hangtime!"` to `dotnet build src`.

### How it's organised

- `sheets/` — the content, as data: perks, triggers, effects, teams, stat cards, opponent scaling, UI. Edit these, then rebuild.
- `src/` — the plugin (C#): one generic engine runs every perk row for either team; Harmony patches hook the game.
- `tools/` — build, the match simulator used for balancing (`python tools/sim.py --help`), emblem and title art generators.
- `assets/` — the emblems and title art (drawn by `tools/emblems.py` and `tools/title_logo.py`).
- `HANGTIME_NOTES.md` — design notes and balance history.

## License

[MIT](LICENSE) — anyone can use, change and share this mod, including in their own projects; just keep the copyright
notice. The fonts in `tools/fonts` keep their own open licenses. Hangtime! itself is not included and belongs to its developers.

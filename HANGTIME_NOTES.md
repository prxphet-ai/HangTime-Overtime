# Hangtime! Overtime — notes

The running log of this autonomous session: what was added or changed, simulator numbers before
and after every balance change, and decisions made without you. The newest entries are at the bottom
of **Log**. **Summary** at the top is kept up to date.

## Summary (kept current)

- **Simulator**: `python tools/sim.py <mode>` (modes: match, teams, scaling, perk, perks, run).
  Results go to `sim_results/` (one JSON per run plus `index.csv`). See *Simulator* below.
- **Opponent scaling**: every number lives in `sheets/scaling.json` (the one place to tune). The game
  (`src/Engine/OpponentScaling.cs`) and the simulator (`tools/simlib.py`) read the same rows;
  `python tools/sim_check.py` proves they compute identical values.
- **Build**: `python tools/build.py [--deploy]` (preflight + generate + build).

## What I found in the codebase (before changing anything)

**Teams** (Hangtime!'s own, read from the game and logged into `sim_data/game_data.json` by the mod):
- Each team is a Unity prefab: `OpponentTeam` (name, banner emblem sprite, voice lines, voice pitch,
  stat *weights*), `PlayerStats` (per-stat level tables, 4 levels each, and a list of built-in perks;
  Shirogane has 7, Kozuki has Agility), and two players (setter + spiker) with AI components.
- AI habits differ per team (`SpikerInput`/`SetterInput`): tip chance, block chance, aggression,
  "defensive structure" (reads the precise landing spot), quick attacks, first-touch attacks,
  double serving, move speeds. These *are* the teams' personalities, so the simulator models them.
- Characters are modular sprites: jersey (`Core_0`), shorts (`Thigh_0`), hair; the mod recolors them
  for new teams.
- New teams (mod) are rows in `sheets/teams.json`, dressed onto a vanilla prefab when they spawn.

**Run / round structure**:
- Classic: practice (0), then bracket slots 1–8; slot 3 and 6 are mid-bosses, 7 a combo team,
  8 the final boss. The game levels opponents with `slot*3+1` points spent by their weights (cap level 3).
- Infinite (mod): round = match number; the bracket slot cycles 1–8 (slot decides the class:
  regular / combo / boss, and the gym). Loops continue Classic after the final.

**Perks and stat cards**:
- The game's perks are `Technique` ScriptableObjects with hooks (OnSpike/OnServe/OnJump/...); cards on
  the upgrade screen are `Logo_technique` entries. Stat cards are `UpgradePair` (title, words, stat
  names); picking one levels those stats up once.
- Mod perks: rows in `sheets/perks.json` run by one generic engine (`src/Engine`), for either team.
  Mod stat cards: rows in `sheets/statcards.json`, points on top of the game's levels.

**Banners and emblems**:
- The gym banner is a generic cloth sprite with a separate team emblem sprite on it
  (`OpponentBanner.mySprite` = `OpponentTeam.banner`). Vanilla emblems are hand-drawn sprites.
- Mod emblems so far: a plain color disc with the name in game text (the "lackluster" ones).

## Simulator

`tools/sim.py` + `tools/simlib.py`. **What it is**: an event model of Hangtime!'s rallies — serve,
receive, set, spike/tip, block, dig — where each step is a probability built from the game's real
numbers (stat tables, AI settings, ball constants exported from your install) and the mod's sheets
(perks, cards, teams, scaling). Perks fire through the same trigger/condition/effect rows the mod
uses, for both teams. **What it is not**: the Unity physics. The game's AI only knows how to play the
right-hand side and real matches take minutes, so a physics-faithful headless run of hundreds of
matches isn't possible. Use it to compare (with vs without a perk, round 1 vs round 10); treat the
absolute win rates as estimates. Model coefficients are in `sheets/sim.json`.

Modes:
- `match --a player --b kagaribi --round 5 --n 500` — one pairing.
- `teams --round 5` — every team vs every team at one round (field win rates).
- `scaling --rounds 1,5,10,15` — a fresh player build vs each team.
- `perk --perk blaze_spike --round 5` / `perks --round 5` — win-rate change from one perk or card.
- `run --n 500` — simulated infinite runs: the player drafts perks/cards round by round.
- Options: `--seed`, `--n`, `--perks-a a,b`, `--cards-a x,y`, `--random-perks-a K`, `--no-power`, `--label`.

## Log

### Step 0 — simulator and the scaling config
- Added `tools/sim.py`, `tools/simlib.py`, `tools/sim_check.py`, `sheets/sim.json`; the mod exports
  `game_data.json` (team stat tables, weights, AI settings, ball constants, its scaling table).
- Rebuilt `sheets/scaling.json` as the single opponent-scaling config (round curves for level-up points,
  a round-1 "base version" stat/speed multiplier, growth past the level cap, perk counts, rarity unlocks,
  theme weighting, player-power factor) and ported it into the game (`OpponentScaling.cs`).
  Opponents now get their own perks first (new teams' signatures, vanilla teams' built-ins), then themed
  random draws; built-in abilities are stripped at spawn and only come back with their perk.
- Baseline (before tuning, seed 1): team vs field at round 5: Kozuki 85%, Shirogane 82%, Amaterasu 67%
  (all above the 65% target), lowest Fujin Wings 32%. Fresh build at rounds 1/5/10/15: 46% / 23% / 8% / 3%
  (round 1 below the 60–75% target). Simulated runs: median loss at round 2, 7% beat round 3.
- Decision: Classic mode keeps the game's own difficulty; the scaling system drives Infinite and Loop.

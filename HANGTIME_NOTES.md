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

### Step 0b — simulator calibration and balance pass 1
- **Model calibration** (`sheets/sim.json`): realistic per-phase timing (dig 0.45 s after a spike, etc.) instead of
  1.2 s per touch (short effects such as Solar Flare's slow were expiring before the next play); spike contact height
  from the game's ball heights (min_height perks could never fire); rebalanced stat weights so the game's own six stat
  cards land within +4.5%..+12% of each other. **Assumption**: the developer meant those six cards to be roughly equal;
  Block-heavy cards (Fortress, Dominator) still read lowest — the model may understate blocking.
- Point mix now: spike kills 47%, spike errors 13%, tip kills 10%, free-ball kills 10%, aces 9%, stuff blocks 6%,
  serve errors 5%; ~8.7 touches per rally.
- **Teams**: built-in vanilla perks now wait for their rarity's unlock round too (Kozuki's Agility is epic -> round 10+).
  Added per-team `points_offset` (teams.json rows and `vanilla_profiles`) and an auto-tuner
  (`python tools/sim.py tune`). Tenzio got a weights override in Infinite (its own weights never level Spike/Bump).
  Team vs field, rounds 2/5/8/12, no boss bonus: before 28.9%..85.4%; after **47.5%..52.6%**.
- **Perks and cards** (sweep `perks --round 3/8`, bands common +2..7%, rare +4..10%, epic +7..14%): nerfed
  Featherstep (+20.7% -> +13.8%), All-Rounder (+19.9 -> +9.5), Quick Hands (+15.1 -> +12.4), Anchor (+14.4 -> +8.2),
  Ace Hunter (+14.1 -> +9.2), Sprint Training / Power Drills / Platform Drills (~+11.5 -> ~+7.3), Solar Flare
  (+14.8 -> +6.0), Cyclone Spike (+14.0 -> +10.4), Zig-Zag Spike (+13.5 -> +9.1), Tailwind (+10.2 -> +6.3, now 6% speed).
  Buffed Tectonic Slam (+3.2 -> +14.4), Iron Wall (+0.8 -> +7.6), Tower (+2.2 -> +9.0), Libero's Instinct, Heavy Hitter,
  Cannon Arm, Wall Practice, Holy Lance, Meteor Smash, Spirit Serve, Time Stop, Cold Snap, Tsunami, Inferno (5 -> 4 touches,
  6 s), and many commons. Fixed real timing bugs: Showboat window 4 s -> 7 s (a tip to your next spike takes ~5 s),
  Quake Spike slow 0.8 s -> 1.6 s (ended before their set). Build-scaling perks (Collector, Hot Streak, Rival Spirit)
  read weak alone by design.
- **Scaling curve** (`sheets/scaling.json`): points/round 1.4 -> 0.7, boss bonus 3 -> 1, combo 1.5 -> 0.5, perks/round
  0.3 -> 0.22, boss perks 1 -> 0.3, base version 0.92 stats / 0.95 move ramping over 8 rounds, growth past the cap from
  round 7 (+3%/round). Fresh build at round 1: 46% -> **72%** (target 60-75%). Typical drafted build per round
  (`run --no-stop`): r1 75%, r3 64%, r5 68%, r6 58%, r8 54%, r10 46%, r12 34%, r15 34% — a gradual slide, no walls
  (before: runs died at round 2-3; round-3 boss 47%).
- Checked in game: round-1 Infinite opponent logged 1.2 points, stats x0.92, move x0.95, no perks; game/sim parity 0 mismatches.

# Hangtime! Overtime — notes

The running log of this autonomous session: what was added or changed, simulator numbers before
and after every balance change, and decisions made without you. The newest entries are at the bottom
of **Log**. **Summary** at the top is kept up to date.

## Summary (kept current)

- **Everything that exists**: `CATALOG.md` (generated from the sheets by `python tools/catalog.py`) lists every team
  (class, element, base, signature perks, emblem), every perk (rarity, element, trigger, description, gameplay effects,
  visuals) and every stat card. Current totals: 34 new teams (+ the game's 10), 154 perks (144 new, 3 reworked, 7 kept),
  47 stat cards. No perk changes the score (enforced by the preflight).
- **Simulator**: `python tools/sim.py <mode>` (match, teams, scaling, perk, perks, run, tune). Results in `sim_results/`
  (one JSON per run plus `index.csv`). See *Simulator* below.
- **Opponent scaling**: every number lives in `sheets/scaling.json` (the one place to tune; `python tools/set_scaling.py
  key=value` edits it). The game (`src/Engine/OpponentScaling.cs`) and the simulator read the same rows;
  `python tools/sim_check.py` proves they compute identical values. Per-team strength: `points_offset` in
  `sheets/teams.json` (auto-tuned with `python tools/sim.py tune`).
- **How scaling works**: round = Infinite match number (or 8 x (loop-1) + slot in Loops). Opponents get
  `points_base + points_per_round x (round-1)` level-ups (+ boss/combo bonus + team offset), spent by the team's own stat
  weights (cap level 3), times the player-power factor (0.7..1.4 ratio, weight 0.3). Stats start at 92% (speed 95%) and
  ramp to 100% over 8 rounds, then grow +3%/round past the cap from round 7 (max x1.5). Perks: 0.22 per round (+0.3 in
  boss slots), the team's own perks first (signatures / vanilla built-ins), then themed random draws (4x weight for the
  team's element), at most 9; rares unlock at round 5, epics at round 10 (built-ins too: Kozuki's Agility waits until round 10).
  Opponents announce their abilities at the start of a match (four names, then "+N more").
- **Balance status**: fresh build wins ~70% at round 1; a typical drafted build slides from ~70% (rounds 1-5) to ~51%
  (round 10), ~43% (round 15), ~27% (round 20). Every Overtime perk/card sits in its rarity band at round 5 (common 2-7%,
  rare 4-10%, epic 7-14% win-rate gain) except deliberately situational ones (tips, comebacks, Collector). Random 5-perk
  builds top out around 65% vs the field; nothing trivializes a match. Teams 46%..53% vs the field at equal rounds (44 teams in the Infinite pools: the game's 10 + 34 new).
- **Perk triggers available** (`sheets/triggers.json`): spike, serve, tip, set, setter_set, dig, block, jump, block_jump,
  passive, streak (clean touches), enemy_streak / win_streak (rallies lost / won in a row), long_rally (touches in the current rally), match_point_against,
  enemy_spike. Effects: `sheets/effects.json` (ball, player, game and visual effects; none can touch the score).
- **Build**: `python tools/build.py [--deploy]` (preflight + generate + build); emblems: `python tools/emblems.py`.
- **Testing in game**: set `DevKeys = true` in the mod's config (off by default for players). F1 fire the lab perk now
  (Shift: as the opponent), F2 / Shift+F2 cycle the lab perk (held from the next match), F3 force the next opponent
  (cycles the new teams), F4 serve, F5 / Shift+F5 give a rally point, F6 win / F7 lose the match, F8 next match is the
  final, F9 log state, F10 every Overtime perk, F11 perk screens show only Overtime perks, F12 Infinite +8 rounds.
- **Difficulty setting**: `[Infinite] Difficulty` in `BepInEx/config/melty.hangtime.overtime.cfg` (default 0, range -4..10):
  opponents play as if it were that many rounds later. A fresh build's first match in the simulator: 0 -> ~69%, +1 -> ~67%,
  +2 -> ~60%, +4 -> ~45%.
- **Open questions for you**: see the end of this file.

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
- Mod emblems at the start of this session: a plain color disc with the name in game text (the "lackluster" ones) —
  replaced by hand-drawn emblems in the game's style (Step 1).

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
- `run --n 500` — simulated infinite runs: the player drafts perks/cards round by round (`--no-stop`: keep going after
  losses to get a win rate for every round).
- `tune --rounds 2,5,8,12` — nudges each team's `points_offset` toward 50% vs the field.
- `builds --round 8` — random 5-perk builds, element stacks and top-perk stacks vs the field (are combos too strong?).
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

### Step 1 — team banner emblems
- Studied the game's own emblems (exported from your install for reference only, never shipped): one flat light color,
  chunky hand-drawn silhouettes, a mascot (kiwi, crab, elephant, fox, heron, ship...) and the name in bold hand lettering.
- New `tools/emblems.py` draws every new team's emblem in that style from code (marker-wobble shapes, per-letter jitter):
  Kagaribi Flame — bonfire basket with a three-tongued flame and sparks; Hyoga Frost — penguin in a scarf with snowflakes;
  Raijin Tech — lightning bolt inside a ring of thunder drums; Fujin Wings — swallow riding gusts; Kaien Marine — whale
  tail over waves with spray; Iwao Stoneworks — rocky peaks with a pickaxe; Yomi Nocturne — bat across a crescent moon;
  Amaterasu Royals — rayed sun wearing a crown. Each uses one accent in the team's color. Spec per team in the new
  `emblem` column of `sheets/teams.json` (mascot, layout top/stacked/split, font, accent); output `assets/emblems/<id>.png`
  plus `assets/emblems/preview.png` (large and 64 px views).
- In game: the emblem replaces the old color disc, the banner cloth takes the team's color (darkened 42% so the light
  emblem reads under the gym lighting, like the vanilla banners), and the emblem is fitted inside the cloth. Checked all
  eight in game (dev key F3 forces the next opponent).
- Fonts for lettering: Permanent Marker (Apache 2.0) and Bangers (SIL OFL 1.1), in `tools/fonts` with their licenses;
  only the rendered emblems ship.
- Decision: vanilla teams keep their own emblems (those are the designs you like).

### Step 2 — scaling verified in game
- Infinite round 10 in game: Kaien Marine got 6.2 level-up points (round curve, its -1.5 offset, low player power),
  stats x1.09 and move x1.07 (growth past the cap from round 7), and one perk — its signature Tidal Arc. Matches the
  config and the simulator (parity check: 0 mismatches over 180 rows).

### Step 3 — content batch 2 (+ simulator timing fix, balance pass 2)
- **New trigger `enemy_spike`** (defensive reactions when the other team spikes), in game and simulator.
- **19 new perks**: defensive/libero — Libero Dive, Read the Play, Guardian Wall (epic), Shadow Step, Tidal Wall,
  Radiant Guard, Iron Curtain, Ice Wall; setter — Quick Set, Back Set, Tempo Master (epic), Tornado Set; serve/offense/
  elements — Jump Float, Meteor Serve (epic), Line Shot, Thunder Serve, Wildfire (epic), Earthquake Serve (epic), Aftershock.
- **7 new stat cards**: Quick Feet, Server's Wrist, Rookie Grit (common); Spike Specialist, Block Party, Setter Duo
  (rare playstyle); Second Gear (epic trade-off).
- **6 new teams** with emblems: Sakuradai Petals (wind, cherry blossom), Kaminari Express (lightning, bullet train),
  Hinode Sparks (fire, rooster at sunrise), Yukimura Snowcats (ice combo, snow cat), Ryujin Tide (water boss, sea
  dragon), Mugen Phantoms (shadow boss, ghost). Offsets auto-tuned: all 20 teams 44%..53% vs the field.
- **Simulator fix**: effects that happen when the ball crosses the net now happen at the crossing, and short slows/stuns/
  buffs count if they overlap a player's last half-second moving to the ball (Earthquake Serve read +0.7%, now +4.5%).
- **Balance pass 2** (sweeps r3+r8): nerfed Second Gear (+20.4% -> ~+12), Tectonic Slam (+15.6 -> lower), Featherstep
  (+14.3 -> +11.2), Quick Hands (+13.1 -> +10.5), Libero's Instinct, All-Rounder, Radiant Guard, Afterburner, Cyclone,
  Shadow Ball, Anchor, several commons. Buffed the weak epics (Time Stop +12% spike, Spirit Serve, Tsunami, Wildfire 5
  touches, Meteor Smash, Earthquake Serve, Inferno 3 touches, Holy Lance, Guardian Wall, Monster Block now also +7 block
  jump) and gave weak perks a small themed second effect (Black Ice faster spikes, Boulder Serve faster serves, Scorched
  Earth +0.1 power, Phoenix Dive counter-spike boost, Undertow wave, Zone Focus spike boost, Aftershock slow, Ice Wall
  block jump). Items outside their rarity band: 30 -> 15.
- **Known outliers, left on purpose**: All In stays strong (~+16%, epic trade-off; its landing-recovery drawback is small
  in the model). Situational commons (Showboat, Rival Spirit, Hot Streak, Quake Spike) and Collector read weak alone.
- **Curves after batch 2**: fresh build r1 70% (target 60-75%); typical drafted build per round r1 70%, r5 67%, r8 53%,
  r10 46%, r12 38%, r15 44%, r20 25% — gradual, no walls; fair chance at round 15.
- Checked in game: all 19 new perks fired through the dev lab with 0 errors; Ryujin Tide spawned with its emblem.


## Open questions

1. **Simulator calibration**: the model's weights were set so the game's own six stat cards are roughly equally useful
   and the point mix looks like a real match. If you have a feel for how strong Block/Jump/Serve are in real play
   compared with Receive/Speed, tell me and I'll recalibrate (`sheets/sim.json`).
2. **Player skill**: the simulator plays your side like the game's AI. If you're stronger than the AI, round-1 win rates
   in real play will be higher than 70%. You can now raise `[Infinite] Difficulty` in the config yourself (see Summary);
   tell me what feels right and I'll make it the default.
3. **Classic mode** still uses the game's own difficulty and opponents (new perks/cards do appear on its upgrade screens).
   Should new teams and the scaling system come to Classic too?
4. **All In** was the strongest stat card (~+16%); trimmed to +13% (top of the epic band). Want it stronger again?
5. **Very late Infinite (round 25+)**: opponents stop getting stronger in stats (x1.5 cap) and only add perks (up to 9).
   If you reach those rounds in real play and it feels easy, I can add a late ramp that isn't raw spike speed (e.g. more
   block/receive, faster setters) — tell me.

### Step 4 — content batch 3 + balance passes 3-4
- **Vanilla cards on the perk screen are now weighted by rarity** like Overtime perks (common 3, rare 2, epic 1; from
  `vanilla_rarity` in `sheets/scaling.json`), so Agility (+36% in the sim, the strongest single item) shows up less.
- **16 new perks**: trade-off/playstyle passives — Overheat (+24% spike, -3% speed), Glacier Body (+18% block, -3%
  speed), Featherweight (+10% jump, weaker blocks), Stone Skin; reactions/team play — Counter Attack, Lightning Rod,
  Spirit Bond, Phantom Block, Sharpshooter, Cannonball Serve; epics — Rally Master, Volcano Slam, Blizzard,
  Typhoon Serve, Abyss Spike, Starfall.
- **4 new stat cards**: Net Rusher, Float Master, Deep Defender, Berserker (epic trade-off).
- **4 new teams** with emblems: Hoshikuzu Stargazers (light, shooting star), Oni Gakuen (fire boss, oni mask),
  Kumo Weavers (shadow, spider in its web), Tsunagi Turtles (earth combo, turtle). Offsets re-tuned: 22 teams 44%..53%.
- **Emblem tool** now warns if any emblem touches the image edge (fixed five that clipped their lettering).
- **Simulator fix**: stunning one player (spiker/hitter) now freezes that player — their team digs worse while it lasts
  and that player can't attack — instead of a random "free ball" chance.
- **Reworked weak designs** (they did little in the game, not only in the sim): Mud Trap now sticks the defender as the tip
  arrives; Ember Tip burns their feet as the tip arrives; Quake Spike shakes them as the spike comes in; Lightning Rod now
  freezes their setter for 2.5 s when they spike (no block on your counter); Aftershock dazes the hitter for 1.4 s.
  Bedrock Block also adds block jump.
- **Balance**: many small nerfs/buffs (sweeps at rounds 3/5/8, 120 matches per team per item). Out of band now are mostly
  situational perks (Showboat, Rival Spirit, Collector) and a few items within ~1% of a band edge.
- **Curves after batch 3**: fresh build r1 70%; typical build r1 72%, r5 68%, r10 50%, r15 39%, r20 25%.
- Checked in game: all 16 new perks fired through the dev lab, 0 errors; Oni Gakuen spawned with its emblem.

### Step 5 — combo check and the Loop button
- New simulator mode `builds`: 60 random 5-perk builds at round 8 (opponents scaled to the player's power): median 46%
  vs the field, best 69%, none above 90%. Strongest found: wind x3 (Typhoon Serve, Dipping Serve, Cyclone Spike) ~70%;
  the five strongest perks stacked 66%. Strong builds exist but nothing trivializes a match.
- Verified in game: beating the final in Classic shows the game's Win screen plus the new **LOOP 2** button (moved up
  under BACK; it sat on the court before); clicking it starts Loop 2 at round 9 of the scaling with a random opponent.

### Step 6 — content batch 4
- **15 new perks** filling thin spots: comebacks — Clutch Gene, Last Breath (epic), Underdog; jumps — Spring Heels,
  Skyhook; tips — Feint Tip, Dink Master, Wind Tip; serves — Iron Serve, Ace Instinct, Sunburst Serve; setter — Tidal
  Surge; epics — Frost Nova, Chain Lightning, Gravity Well (defensive: their spike stalls and loses half its power).
- **4 new stat cards**: Clutch Training (common), Iron Body, Track Star (rare playstyle), Tank (epic trade-off).
- **4 new teams** with emblems: Fukurou Owls (light, owl on a branch), Tako Tentacles (water combo, octopus),
  Byakko Tigers (lightning boss, white tiger), Iwagami Golems (earth boss, stone golem). All 32 teams (10 vanilla + 22 new) now 43%..53% vs the field.
- **Simulator**: tip speed now matters when digging a tip (faster-tip effects were worth nothing before, vanilla Long
  Toss too); defensive effects that slow their spike now also lower its speed. Note: tip perks read lower in the sim
  because its AI tips about 1 attack in 5 — for a player who tips on purpose they are worth more.
- Balance after tuning (round 5 sweep): Chain Lightning +12.9%, Last Breath +9.6%, Gravity Well +9.5%, Frost Nova +7.5%,
  Feint Tip +6.9%, Track Star +9.6%, Tank +11.2%, Clutch Training +6.7%; situational ones (Underdog, Clutch Gene) lower.
- Checked in game: all 15 new perks fired through the dev lab, 0 errors; Byakko Tigers spawned with its emblem.

### Step 7 — late-round check in game
- Played Infinite to round 18 (dev keys): Kumo Weavers got 12.6 level-ups, stats x1.33, speed x1.14 and three perks
  (its signatures Mud Trap and Shadow Step, then a random rare, Halo Dig). The announcement types out in full and stays
  readable; the list now starts on its own line ("Our abilities:" / "Mud Trap, Shadow Step, Halo Dig!").
- Fixed a dev-key bug (players never see it): F5/F6/F7 also worked on the upgrade screen, because the game's
  GameManager carries over between scenes, and loaded that screen a second time, which froze its buttons. Those keys now
  only work during a match.

### Step 8 — content batch 5
- **17 new perks** for the thin spots (shadow/ice/water/light commons, wind and element-free epics, more dig/set/block/jump
  triggers): Umbral Block, Shade Dig, Ice Skates (passive trade-off: faster, slightly lower jumps), Chill Touch, Ripple Dig,
  Splash Block, Glint, Guiding Light, Grounded Receive (commons); Frost Fang, Beetle Horn, Foxfire Feint, Lightning Step
  (rares); Tengu Gale, Flow State (6 clean touches in a row: stronger, higher, quicker for 8 s), Nine Tails, Hercules Block
  (epics). None touches the score.
- **4 new stat cards**: Spring Loaded (common), Playmaker, Line Judge (rare playstyle), Daredevil (epic trade-off: big attack,
  weak defense).
- **4 new teams** with new emblem mascots: Kitsune Foxfire (shadow regular, fox mask with foxfire), Karasu Tengu (wind boss,
  crow tengu), Fubuki Wolves (ice boss, wolf howling at the moon), Kabuto Beetles (power combo, rhinoceros beetle). Fills the
  gaps: first power team, first wind and ice bosses. Checked in game: Kitsune Foxfire spawned with its emblem and colors.
- **Team balance** (`tune`, rounds 2/5/8/12): all 36 teams 47.9%..52.1% vs the field.
- **Perk balance** (round 5, 120 matches per team): first pass had Daredevil +17.5% (too strong) and six perks at ~0%
  (effects too short to matter before the next relevant touch: e.g. a 0.5 s freeze on their digger wore off before
  their attack). After two passes everything new is inside its rarity band: Daredevil +13.1%, Hercules Block +11.5%,
  Nine Tails +9.8%, Flow State +9.1%, Tengu Gale +7.9%, Playmaker +6.1%, Line Judge +5.0%, Umbral Block +5.0%,
  Chill Touch +4.9%, Frost Fang +4.5%, Lightning Step +4.1%, Splash Block +3.9%, Shade Dig +2.8%, Ripple Dig +2.5%.
- **Combos** (60 random 5-perk builds at round 8): best 59.5%; lightning x3 55.6%. Nothing trivializes a match.
- **Late rounds**: with `--no-stop` (the simulated player keeps drafting after losses) the per-match win rate falls to
  ~27% at round 20 but drifts back up past round 25. Cause: opponent spikes get so fast that digging is already
  near-impossible, and in the model extra speed only adds spike errors; the player meanwhile holds 20+ perks. I tried
  raising the stat caps (x1.5 -> x2.2) — no effect, and very fast balls risk physics trouble in the real game, so the stat
  caps stay at x1.5 (verified in game). The opponent perk cap went 7 -> 9 so late opponents keep gaining abilities.
  In practice this is unreachable: simulated full runs end at median round 3, 2% pass round 10, ~0% reach round 15
  (the AI plays your side; you will do better). Logged as open question 5.
- Dev tool fix this round: none needed; all 17 new perks fired through the dev lab as both sides with 0 errors.

### Step 9 — balance pass 6 (older outliers)
- Combined sweep (rounds 5 + 8). Above band: **All In** (epic card) +16.9% -> **+13.0%** (Spike/Jump 1.75 -> 1.4 each);
  **Spike Specialist** (rare card) +11.3% -> +9.6% (Spike 2 -> 1.6).
- Near-zero perks buffed (round 5, before -> after): Lightning Rod +1.5% -> +5.8% (now also makes their whole team flinch
  0.3 s; always fires, cooldown 2), Counter Attack +2.3% -> +8.0% (quick step toward the ball + next spike +0.7),
  Storm Serve +0.8% -> +4.1% (bigger, longer jitter), Hot Streak -0.3% -> +3.1% (0.2 power per rally won in a row, max
  0.6), Sharpshooter +0.7% -> +2.6% (70% of spikes, stronger bend), Lightning Reflex +1.8% -> +2.1% (+30% speed for the
  rest of the play), Underdog -0.5% -> +1.5% (after two lost rallies; stronger, longer), Rival Spirit -0.2% -> ~+1%
  (20 s window).
- Still reading low on purpose: situational perks — Showboat and the tip perks (the simulated AI tips 1 attack in 5),
  Collector (scales with how many abilities you own; the sweep tests each perk alone), Rival Spirit/Underdog/Clutch Gene
  (comebacks only fire when behind). Many rares/epics flag LOW only at round 8, where every delta shrinks (base win rate
  ~23%); at round 5 they are in band.
- Curves unchanged: fresh build r1 69%; typical build r1 70%, r5 66%, r10 53%, r15 39%, r20 ~27%.

### Step 10 — content batch 6 + a new trigger
- **New trigger `win_streak`** (n): fires once when your team wins its n-th rally in a row — the snowball mirror of
  `enemy_streak`. Added to the engine (`Engine.OnRallyEnd`), the simulator, the preflight and `sheets/triggers.json`.
  Seen firing in game ("P Momentum <- win_streak" on the first rally won).
- **11 new perks**: snowball — Momentum, Tailwind Rush (commons, n=1: once per winning run), Avalanche, King's Roar
  (rares, n=2), Firestorm (epic, n=2); comeback — Rebirth Flame (epic, after three lost rallies); others — Boar Rush (run-up
  jump that charges your spike), Tusk Serve, Shock Tip (tip that freezes whoever digs it), Voltage (4-touch streak), Tidal
  Pull (drags their spike back for a moment). All fired through the dev lab as both sides, 0 errors.
- **4 new stat cards**: Stamina (common), Big Blocker, Jet Setter (rare playstyle), Overload (epic trade-off).
- **4 new teams** with new emblems: Shishi Lions (power regular, lion in its mane), Inoshishi Boars (earth regular,
  charging boar), Hinotori Phoenix (fire combo, phoenix), Unagi Eels (lightning combo, electric eel). Shishi Lions checked
  in game. Hinotori is built on the Aomori prefab, which needs a big points offset like every Aomori-based team (+7.2).
- **Design lesson from the simulator**: a rally plus the pause after it is ~10 s, so 8–10 s buffs from rally-end triggers
  last about one rally and did nothing (first numbers ~0%). Rally-end buffs now last 18–35 s (2–3 rallies). Underdog
  re-tuned with this (+4.3%).
- **Balance** (round 5, before -> after where changed): Momentum +2.9%, Tailwind Rush +5.1%, Avalanche +9.0%,
  King's Roar +4.6%, Firestorm +9.7%, Rebirth Flame +3.5% (comeback: fires only when behind), Boar Rush +4.3%, Tusk Serve
  +2.6%, Shock Tip +1.8% (tip perk, see the tip note), Voltage +2.0%, Tidal Pull +2.5%; cards Stamina +6.6%, Big Blocker
  +5.3%, Jet Setter +4.6%, Overload +15.1% -> +11.2%.
- Teams 46.9%..52.1% vs the field; fresh build r1 70%; typical build r1 72%, r5 68%, r10 51%, r15 43%; best random
  5-perk build at round 8 65% (Avalanche + Guiding Light + Halo Dig + Iron Serve + Spirit Serve), wind x3 64%.

### Step 11 — late opponents' announcement
- With the perk cap at 9, the "Our abilities" bubble could run to four lines. It now names four and adds "+N more".
  Checked in game at round 42: Namasito with 9 perks (stats x1.50, speed x1.20; water-themed draws such as Tidal Pull and
  Tsunami) — three readable lines.
- Not changed on purpose: the game's own **Agility** card is still the strongest single pick in the simulator (+36%). It is
  already offered at epic weight (the rarest); I did not alter vanilla card effects. See open question 1 (how much speed is
  worth in real play).

### Step 12 — long rallies: a new trigger and 6 endurance perks
- **New trigger `long_rally`** (n): fires once per rally, for each team holding the perk, when the rally reaches n accepted
  touches by both teams together. In the simulator ~39% of rallies reach 6 touches, ~25% reach 8.
- **6 new perks** (round 5 gain): Second Breath (common, 6 touches: quicker and springier for the rest of the rally,
  +4.5%), Winter's Patience (common ice, their team slows, +5.0%), Tidal Rhythm (common water, stronger spikes and faster
  sets, +3.1%), Stamina War (common earth, 8 touches: blocks and feet, +5.7%), Heat Haze (rare fire, +7.8%), Starlight
  Finale (epic light, 8 touches: their team freezes and your spikes shine, +8.4%). First numbers had Second Breath at
  +9.5% and Heat Haze at +11% (trimmed); Tidal Rhythm stayed ~3% however strong its spike buff, so it became a common.
- All six fired through the dev lab as both sides, 0 errors. The trigger itself is checked by code path only (an idle
  player can't make a long rally in a test); its count lives next to the streak count in `Engine.OnAcceptedTouch`.

### Step 13 — difficulty setting
- New config entry `[Infinite] Difficulty` (int, -4..10, default 0): opponents in Infinite and Loops are scaled as if it
  were that many rounds later (or earlier). It moves every knob at once — level-ups, stat ramp, perk count, rarity unlocks —
  so the tuned curve stays intact, just shifted. Checked in game: Difficulty 2 -> match 1 scaled as round 3.
  Simulator, fresh build vs the field: +0 ~69%, +1 ~67%, +2 ~60%, +4 ~45%. Default left at 0 (the brief's 60-75% target).

### Step 14 — content batch 8 (last empty slot/element pairs)
- **4 new teams** with new emblems: Kongo Titans (power boss, gorilla pounding its chest), Tombo Dragonflies (wind combo,
  dragonfly), Hotaru Fireflies (light combo, firefly), Kage Ninjas (shadow combo, masked ninja with shuriken). Every
  element now has a team in every class it fits. Kage moved from the Aomori to the Ten-Roku prefab (Aomori-based teams are
  weak by default); Tombo keeps Aomori at the +8 offset cap (45.7% vs the field).
- **9 new perks** (round 5 gain): Titan Slam (epic, +9.8%), Shadow Clone (epic, +8.0%), Chest Pound (+5.0%), Dragonfly
  Dart (+5.2%), Firefly Swarm (+7.0%), Shuriken Serve (+4.4%) (rares); Updraft Set (+7.4% -> +5.0%), Lantern Light (+5.2%),
  Smoke Bomb (+3.2%) (commons). All fired through the dev lab as both teams, 0 errors.
- **2 new stat cards**: Utility Player (rare, +7.8%), Juggernaut (epic trade-off, +7.5%).
- Teams 45.7%..53.0% vs the field.

### Step 15 — title screen: "Hangtime! Overtime" and credit
- The game's own HangTime! logo now has **OVERTIME** under it, drawn in the logo's style (white letters, thick yellow
  outline sampled from the logo, letters slightly turned), and a credit line **by averageprxphet** below that. Art drawn
  from code by `tools/title_logo.py` (`assets/title/`, shipped in the plugin's `title` folder); placed by
  `MenuPatches.AddTitleLogo` as children of the logo sprite so they follow its intro animation. The title buttons move
  down a little to make room. Positions are rows in `sheets/ui.json` (title_overtime, title_credit, title_buttons_shift).
- First version seen by you in game: too big, too low (overlapped CLASSIC) and tilted the wrong way. Now tilted -6 deg like
  the logo (it slopes down to the right), half the logo's width, tucked under it, credit smaller, buttons moved down
  2.4 units. Checked with a to-scale mock on your screenshot; not yet re-seen in game (you were mid-match).
- Second round: you asked for both lines straight under the logo. OVERTIME and the credit are now upright and centered
  under HangTime!. Found why the buttons never moved down: the game has more than one object called "Button group", and I
  was moving the wrong one. The title menu is now found through its CLASSIC button, the same way as the INFINITE button.
- Third round: buttons moved down only 0.45 units, so CLASSIC sits just under the credit line.

### Step 16 — auto-save and Continue (Infinite and Classic)
- **What is saved** (`<persistent data>/HangtimeOvertime_runs.json`, separate from the game's own `save.json`; code in
  `src/Core/RunSaves.cs`): mode, bracket slot / Infinite round / loop, wins, the Classic line-up, run timer, every stat
  level, every ability (Overtime perks by id, the game's own by class), Overtime stat card points, limit breaks, and the
  opponent of the match about to be played with the perks it drew. Two slots — Infinite and Classic (Loops count as
  Classic) — so one never overwrites the other. Co-op runs and the tutorial are not saved.
- **When**: at the start of every match, when the upgrade screen opens after a win, right after a perk/stat card pick,
  and again when the game closes or the window loses focus. **Decision: closing mid-match resumes at the start of that
  match** (same opponent, same opponent perks, your abilities and stats as they were), not mid-rally.
- **Cleared** when the run ends: a lost match, or winning the Classic final (LOOP 2 starts saving again at its first match).
- **Title screen**: a mode with a saved run shows "CONTINUE · ROUND 7" (or "MATCH 3") under its name. Clicking it opens a
  panel: the saved run (round, opponent, abilities, stat boosts, time) with CONTINUE / NEW RUN / BACK; NEW RUN asks
  "START A NEW ... RUN? THIS DELETES YOUR SAVED RUN" before overwriting.
- **Versioned**: the file has a version number and each slot is read through a migration step; a save that can't be
  read (corrupt, or from a newer mod version) is kept aside as `.bad`, the player gets a notice on the title screen, and
  the game starts fresh instead of crashing.
- **Tested in game**: Infinite — saved at round 1, upgrade, pick and round 2; killed the game mid-match; relaunched, CONTINUE
  put me back at round 2 vs the same team with the same perk. Killed it on the upgrade screen; CONTINUE reopened that
  screen (with the beaten team's comment), the pick went through and round 3 started with both perks. At round 12 the
  opponent came back with exactly the same two perks. Classic — practice -> stat card -> match 1 vs Kozuki, killed,
  CONTINUE restored match 1 vs Kozuki with the stat card. Losing a resumed run cleared its save. NEW RUN -> confirm -> NO
  and BACK both return cleanly.

### Step 17 — character creator
- **Where**: a CUSTOMIZE button on the title screen, above your player (left of the menu) — the last stop before starting a
  run. Code: `src/Engine/Look.cs` (options, saving, applying), `src/Patches/CreatorMenu.cs` (the screen),
  `src/Patches/LookPatches.cs` (where the look is applied).
- **Options**: hair style (Spiky = your own hair, Round, Big, Flame, Swoop, Long — the game's own hair art, placed the way
  the game's characters wear it), hair colour (14 + original), skin tone (7 + original), jersey colour (primary), shorts &
  number colour (secondary), jersey number 0-99, build (Regular, Slim, Broad, Tall, Compact — proportions only), headband,
  wristbands, knee pads (none or a colour) and shoe colour.
- **Screen**: a large live preview of your player (and the title-screen player updates too); mouse arrows on every row,
  or keyboard/gamepad: up/down picks a row, left/right changes it, Enter/A also advances, Esc/B closes. RANDOMIZE, RESET
  (back to the game's look) and DONE. The title menu is switched off while it's open.
- **Saved** in `<persistent data>/HangtimeOvertime_look.json` and applied everywhere player one is drawn: the title screen,
  every match, and the upgrade / win / lose scenes (those draw you as a separate character; it is found by the number 13
  it wears). The look is global rather than part of a run save, so a resumed run simply uses your current look.
- **Cosmetic only**: nothing in it touches stats, perks, hitboxes or the simulator.
- **Decisions**: no eye colour — the game has a single eye sprite and tinting it would colour the whites too. Body types
  are proportions of the game's one body sprite (there are no other body models). Skin tones multiply the game's own
  shading, so "Original" is the lightest. Your own spiky hair is drawn in colour, so a recolourable copy is made at runtime.
- **Tested in game**: every hair style in a bright colour, skin/build/extras up close (headband under the fringe,
  wristbands before the hand, knee pads at the knee), RANDOMIZE -> DONE then the title, a practice match and the upgrade
  screen all showed the new look (white Round hair, gray #54 jersey, green shorts); RESET -> DONE restored the original.
- I removed the test run save my testing created (your Infinite slot starts empty); the appearance file is at the default.

### Step 18 — stats chart shows the Overtime stat cards (v0.2.1)
- Bug you found: picking one of the new stat cards didn't change the stats chart on the upgrade screen. The chart
  (`StatChart.SetPoints`) only draws the game's own stat levels; Overtime cards keep their points separately and apply them
  in matches. A postfix now moves each chart point by the card points on its axis (same units as a level-up): Receive ->
  Bump, Spike, Jump, Block, and Serve on both serve axes. Speed, Set and Recovery have no axis on the game's chart.
  Checked in game: Iron Body (Block +1.5, Receive +1) stretched the Block and Bump points, Block 1.5x as far.
- Dev key F11 (Overtime-only cards) now also limits the stat card screens to the Overtime cards, for testing.

### Step 19 — bug fix: spiky hair lost its top when recoloured
- **Cause**: the Spiky style is the player's own hair, which the game draws in colour, so the creator makes a recolourable
  copy at runtime. The copy was cut out of the game's texture with a hand-made y-flip, but that sprite lives in a
  2000x2000 atlas (at y=1216); the flip read y=549 instead — a different strip of the sheet — so only part of the hair
  came out. The other styles use the game's own white hair sprites and never had the problem.
- **Fix** (`Look.Tintable`): copy the whole texture through a render texture and cut the sprite's rect out in texture
  coordinates. Also fixed: the creator's preview copied the title-screen player while it already wore the saved look, so
  "Original"/RESET showed the saved colours; it now copies the player as the game draws it.
- Checked in game: Spiky in all 15 colours; Round/Big/Flame/Swoop/Long in black, white and blue; saved -> restarted ->
  title screen, a practice match and Versus all show the full spiky hair.

### Step 20 — Versus mode (1v1)
- **Where**: VERSUS button on the title screen (left column, above CUSTOMIZE).
- **Join screen**: each player presses a button on their own input to join — any of W A S D (or Space) = the WASD set,
  any arrow key (or Enter) = the arrow keys, A or Start on a gamepad = that gamepad. Two keyboard sets, keyboard + pad, or
  two pads all work. Left/right picks a team, jump = ready; the match starts when both are ready (or START). Esc = back.
  Player 1 plays the left side as Hoshiyumi in their creator look, or in a new team's colours; player 2 picks any team
  (the game's or a new one) for the right side.
- **Controls in a match**: move left/right, jump/hit/spin-serve = W / Up / A (or right trigger), receive/float-serve =
  S / Down / X (or left trigger). Serving works like the game: step behind your back line, jump, then hit.
- **Who is who**: player 1 is the game's own player with the game's bot setter; player 2 takes over the right team's
  spiker (its `SpikerInput` AI switched off, and the ball's right-side server set to it), with that team's AI setter.
  Both humans are driven through the same calls the game's input and AI use (`SetXInput`/`UpInputPressed`/
  `DownInputPressed`), from `VersusDriver`; the game's single-player input is blocked in Versus so a key can only ever
  move its own player.
- **Fair start**: the right team uses the player's own stat tables at level 0, the same move speeds, no built-in
  abilities — team choice is cosmetic; builds come only from the picks.
- **Format (ROUNDS-style)**: a round is a normal match to 5 points; first to 5 round wins takes the match. After each
  round the **loser** picks one of 3 upgrades (perks — Overtime and the game's own opponent-safe ones — and stat cards,
  about 1 in 3 a card; no duplicates of owned perks; stat cards stack). Only the loser's input works on that screen.
  Between rounds: round score and both builds. Picks carry over. Perks fire for both sides (e.g. P1 Counter Attack on
  P2's spikes, P2 Spring Heels on jumps); P2's stat cards apply only to P2 (seen: spike 1.05 -> 1.75).
- **Match end**: both final builds and CONTINUE / REMATCH / MENU. **Decision: CONTINUE keeps both builds and raises the
  target in 5-round chunks** (first to 10, then 15...); loser-picks stays on. REMATCH starts fresh; MENU goes to the title.
- **Config, one place**: `sheets/modes.json` -> `versus` (points_per_round 5, rounds_to_win 5, continue_rounds 5,
  offer_size 3, stat_card_share 0.34), generated into `Generated.VersusRules`.
- **Run saves untouched**: Versus never saves or clears the Infinite/Classic slots (checked: the save file was
  byte-identical before and after a long Versus session), and leaving Versus by any path (MENU, pause -> MENU, starting
  another mode) clears both builds — a CO-OP match started right after had no perks or cards.
- **Disconnects**: if a player's gamepad goes away the match pauses with a notice; reconnecting it resumes, or pressing A
  on any other free gamepad hands that player's slot to it.
- **Built for later**: a match is a `VersusSetup` — a list of slots (side, role hitter/setter, Human/AI/Remote, input).
  - **1v2** (one player + AI teammate vs 2 friends) and **2v2**: add a second Human slot on a side. The left side already
    has the game's co-op player-2 object (`MultiplayerController.player2Object`, shown instead of the bot setter); on the
    right, a second human needs the right setter's `SetterInput` switched off and driven like the spiker. The join screen
    needs 3-4 columns and the pick screen needs a rule for which loser picks (both, in turn, is the natural ROUNDS choice).
  - **Online**: add `SlotControl.Remote`, whose `PadState` arrives from the network instead of a device (the game ships
    Steamworks.NET, so Steam P2P is available); the host runs the match. The driver, rounds and pick screens stay as they are.
- **Tested in game** (2026-10-09): joining on WASD + arrows; team picks; real-key serves for both sides; long rallies where
  each AI setter sets for its own human and both humans receive and spike (driven by a dev-only autopilot, F4 with
  DevKeys on, through the same input path); rounds to 5 counted by the mod; pick screens for both players (the other
  player's keys do nothing); perks and stat cards on both sides; match end 5-1; CONTINUE (first to 10, builds kept);
  REMATCH (fresh); MENU; pause, resume, pause -> MENU; a second Versus match starting fresh; a virtual gamepad unplugged
  and replugged mid-match; CO-OP afterwards clean; 0 errors in the log.
- **Known issues / not done**: no real gamepad was available, so pads were tested with a virtual device (joining and
  playing with a physical pad is untested). (The left banner issue is fixed in Step 22.) The
  game's own limit-break effects only show on the left side (the game hard-codes them to x<0). The AI-vs-AI simulator
  does not model Versus yet (a build-vs-build mode would be the way to balance the draft); the last line of your brief
  about the simulator was cut off.

### Step 21 — Versus camera; limit breaks in Versus
- The game's camera leans towards player 1 (it tracks the left player and the last touch), so the right side was cut off.
  In Versus it now stays centred on the net and zooms out until both back lines (and the space behind them) are in view;
  the game's hit shake and zoom-on-kills still play. Config: `sheets/modes.json` -> `versus.camera_half_width` (47 world
  units each side) and `camera_follow` (0 = fixed on the net). Checked in game: equal margins left and right, both
  servers visible, steady through rallies.
- Limit break in Versus: not just the visuals — it can't be reached at all. A limit break is a stat at the game's level 4
  (bigger stat values, plus the limit serve that sometimes can't be returned); it comes only from the game's own stat
  level-ups and its Limit Break card, and Versus offers neither (it uses Overtime stat cards and opponent-safe perks).
  Classic/Infinite are unchanged: limit breaks work there for you as in the game (opponents never get them in the game
  either). Adding them to Versus would need the effects to work for the right side too (the game hard-codes them to x<0).

### Step 22 — Versus: player 1's team on the left banner
- The gym has two banners under one `Banners` object: the left cloth `Banner` with the Hoshiyumi emblem `Player Banner`,
  and the right cloth `Banner (1)` with `Opponent Banner` (the only one the game changes). When player 1 picks a new team,
  the left emblem now shows that team's emblem on its darkened team colour, sized to the cloth and placed at the same spot
  as the right emblem (the left emblem object hangs lower on its cloth). Hoshiyumi keeps the game's own banner.
  Checked in game: Kagaribi Flame and Raijin Tech on the left, opponents' banners on the right unchanged.

### Step 23 — Versus 1v2 and 2v2
- **Join screen**: mode tabs 1 VS 1 / 1 VS 2 / 2 VS 2 at the top (click, or player 1's DOWN key before readying). Each
  side's column lists its seats; players fill them in join order: 1v1 = P1 left, P2 right; 1v2 = P1 left (with the AI
  setter), P2 + P3 right; 2v2 = P1 + P2 left, P3 + P4 right. Four keyboard sets now: W A S D (Space), arrows (Enter),
  I J K L (H), numpad 8 4 5 6 (numpad Enter), plus any number of gamepads. Each side's first player picks its team;
  everyone readies; the match starts when every seat is filled and ready. Switching modes drops players past the new
  seat count and un-readies everyone. The title button reads VERSUS / 1V1 · 1V2 · 2V2.
- **Who is who**:
  - left partner (2v2) = the game's own co-op player 2 (`LocalInputManager.player2`; the game is set to two players, so
    it shows instead of the bot setter and the game alternates the left serve between the pair as in CO-OP);
  - right second player (1v2, 2v2) = the right team's setter with its `SetterInput` off and `setter = false`, so it
    jumps and plays like any player (the same as the left co-op partner), with the partner's move speed.
  - Two humans on the right alternate serving (postfix on `BallMovement.opponentGetPoint`, mirroring how the game
    alternates the left co-op pair).
  - The game's co-op input (the P2 actions and the fixed-key `ManualInputRouter`) is blocked in Versus, so each key set
    or pad only drives its own player; its "connect two controllers" message is hidden.
- **Picks**: the losing side's players take turns (P1, then P2 next time the left loses; same on the right); only the
  picker's input works. Round and match screens name sides "P1", "P1 + AI", "P3 & P4". Any player can choose CONTINUE /
  REMATCH / MENU at match end.
- **Tested in game** (2026-10-09, four keyboard sets, dev autopilot through the same input path):
  - 2v2: all four joined; full rallies (receive, set, spike) on both sides; the left pair alternated serving and so did
    the right pair; picks went P1, P2 (left) and P3, P4 (right) with the other players' keys ignored; a perk and stat
    cards on both sides; match end 2-5 "P3 & P4 WIN THE MATCH!"; CONTINUE from P4 (first to 10, builds kept).
  - 1v2: P1 + AI setter vs P2 + P3; P1 spiking off the AI's sets; a 3-5 round; "P1 + AI" picker.
  - 1v1 still works (a third key set can't join); pause -> MENU -> CO-OP afterwards is plain co-op with the game's own
    keys driving both players; run save and game save byte-identical; 0 errors.
  - The dev autopilot now aims for where the ball will land (without an AI setter it never reached serves in time).
- **Known**: no physical gamepads to test with (keyboards and a virtual pad only). The right team's setter has the
  right team's look; the left partner wears P1's team colours like the rest of the left side.

### Step 24 — fix: opponents too low to spike (Infinite/Loop)
- **Bug**: some teams couldn't jump high enough to reach sets, or played passively and never spiked. Cause: opponent
  scaling multiplied every stat level by the round multiplier, Jump included. At round 1 that is x0.92 (rising to x1 by
  round 9), and every team's table starts at Jump 61, the height the game's AI is built around (it only goes for high
  sets above 70 and times its swing from jump height). Teams that got no early Jump level-up played at about 56: of 45
  teams, 41 averaged under 60 at round 1, and teams with Jump weight 0 or a negative points offset (Namasito Academy,
  Raijin Tech, Kaminari Express, Inoshishi Boars...) stayed under 61 until about round 8. Serve jump (60 -> 55) and block
  jump were cut the same way. The simulator didn't show it because it treats jump as a smooth bonus with no reach limit.
- **Fix**: Jump, ServeJump and Block are "reach" stats with their own multiplier, the stat multiplier clamped to
  `reach_min_mult` 1.0 .. `reach_max_mult` 1.12 (sheets/scaling.json). They never drop below the table, and late rounds
  top out near the player's limit-break jump (71 -> about 80) instead of x1.5 (106). Spike power, receive, serve speed,
  move speed, level-ups and perks still scale as before. Same change in tools/simlib.py.
- **Balance**: opponents at full jump are stronger early; a fresh player's simulated round-1 win rate fell from 70% to
  61%. `points_base` 2.0 -> 1.0 brings it to 64% (target 60-75%), and rounds 5-15 sit near the old curve
  (r5 44%, r8 24%, r10 12%, r15 5%).
- **Checked**: game/sim parity 0 mismatches after re-exporting the game data; in game, Infinite round 1 vs Sakuradai
  Petals logged "stats x0.92 (reach x1.00) ... jump 61.0" and its spiker took full approaches and spiked, its setter
  blocked. The match log now prints each opponent's jump. Classic (the game's own difficulty) and Versus (fixed level-0
  tables) are not affected. Saves restored byte-identical after the test.

### Step 25 — Online Versus
- **How to play**: VERSUS -> HOST ONLINE. The host listens on Steam (a friends-only lobby; the button becomes INVITE
  (STEAM), which opens Steam's invite dialog) and on direct IP (UDP port 27515) at the same time. A friend joins by
  accepting the Steam invite / "Join game" in the friends list (the game opens VERSUS and connects by itself, also from
  a cold start via `+connect_lobby`), or with JOIN BY IP (address in `[Online] JoinAddress`). Then everyone joins seats
  with their own keys as in local Versus; the friend's players show as e.g. "W A S D (NAME)". Any mix works: 1v1,
  1v2, 2v2, with players on either machine (seats fill in join order). Different Overtime versions are refused with a
  message.
- **How it works**: the host plays the real match. The client's copy of the round is a mirror: players and ball follow
  the host's snapshots (50/s, shown ~90 ms behind and interpolated, `[Online] BufferMs`), with their physics off so the
  copy never scores or touches the ball by itself; animation states come with each snapshot. Everything the host's game
  produced is replayed at the same moment: every sound, the ball's voices, dialogue lines, camera shake/zoom/tilt/
  hit-stop, the ball's particle effects, jump dust, and each point (the client runs the game's own point routine, so
  its score, next server and point effects match; the score is also checked against the snapshots). The client's
  devices travel to the host as running press counts (a lost packet delays a press, never drops it) and are read
  exactly like local devices (`InputKind.Remote`), so menus, picks and matches needed no other changes. The join screen,
  pick screens and match-end screen are run by the host and drawn on the client from the host's data; only the picker's
  keys count, whichever machine they are on. Each round waits for the client to finish loading (up to 15 s).
- **Online games keep running in the background** (alt-tab must not freeze the other player); the setting is restored
  when the session ends.
- **Disconnects**: no packets for 8 s (`[Online] TimeoutSeconds`) ends the session. The host pauses with "P2 LOST THE
  CONNECTION" and a free key set or gamepad can take that player over (or ESC -> MENU). The client returns to the title
  with the reason ("THE HOST STOPPED RESPONDING", "THE OTHER PLAYER CLOSED THE GAME", "THE HOST LEFT THE MATCH").
  Leaving by MENU or closing VERSUS ends the session for both.
- **Tested** (2026-10-12, two copies of the game on this PC over direct IP, `-ovt-host` / `-ovt-join` / `-ovt-auto`):
  joining and readying from the client; menu mirror; 1v1 full match (5-2 in rounds, several 5-4) with P2 played from the
  client over the network; the client's pick (right + jump on its own keys took the second offer); REMATCH chosen from
  the client; 2v2 with two players per machine, the host pair's picks alternating; side-by-side screenshots matching
  (positions, score, animations); killing the client (host paused with the notice) and killing the host (client back to
  the title with the reason); 0 exceptions in either game's log; saves unchanged. Steam: hosting under Steam created
  the lobby and showed INVITE (STEAM).
- **Fixed while testing**: the host stopped running when its window was in the background (hence "no answer"); the
  client's point routine crashed when the right side scored (the right server is only registered by the AI the mirror
  turns off); the first press from a new client device was lost; the dev autopilot's serve could get stuck at low frame
  rates; the host autopilot was also playing the client's players (test-only).
- **Not done / known**: joining over Steam needs two Steam accounts on two PCs and is untested end to end. Perk visuals
  made by Overtime's own effects code are not mirrored yet (their effect on the ball is, through its movement). The
  client feels its ping on its own player (no prediction), plus the ~90 ms buffer. Two machines per match (up to four
  players across them). Direct IP from outside your network needs UDP 27515 forwarded on the host's router. The
  client's pause menu is local (the match keeps going).


### Step 26 — fix: stuck in the block pose after low-Block stat cards
- **Bug**: with Block lowered by stat cards (or debuffs), a block jump could leave the player frozen in the block pose,
  unable to do anything. Cause: the game sizes the block box from the block value with a formula only ever used at 73
  and up (offset = (block - 73) / 9 + 0.3). Below 73 the box sinks under the player's feet; on the way down it lands
  on the floor first and holds the player just above it (measured at block 66: box bottom -9.99, feet -9.29, player
  hovering at y -7.51 instead of -7.99), and the game only ends blocking when the feet land.
- **Fix** (`ControllerPatches.BlockBox`, MakeBlock prefix/postfix): the box's bottom never goes below where the game puts
  it at block 73, and its height never under 1. Low Block still costs a lower block jump and a smaller box above.
  Values of 73 and up are untouched.
- **Checked in game** (local Versus, dev Ctrl+F9 forcing player 1's block to 66/58/50/40/30 and block-jumping every time
  the ball crossed): reproduced the soft-lock at 66 before the fix; after it, 34 block jumps across all values all
  landed and recovered; 0 errors.

### Step 27 — online: perk effects on the client, ping readout, smoother picture
- **Perk effects** (`src/Online/FxSync.cs`): perks only run on the host; what they draw now reaches the client as events
  and is drawn again there: every visual effect kind through `Vfx.Run` (bursts, flashes, auras, afterimages, bolts,
  rings, trails, tints, slow-mo) on the matching player, element call-outs, stuns (flicker + bolt), ground zones, ball
  invisibility and decoys (the host's decoy path, not a new random one). Shake/zoom/sounds already travel with the
  camera and sound events. Checked with two copies and the host firing a random perk every 3 s (`-ovt-fx`): every
  effect type arrived and showed on both screens (e.g. TITAN SLAM! call-out, stun, bursts), 0 errors.
- **Ping readout** (`PingHud`): top-right on both screens while connected (menus and matches), round trip in ms,
  green < 80, yellow < 150, red above.
- **Freezes**: the client placed its picture by the single quickest packet it had seen, so one quick packet made it
  run ahead of the snapshots it had and hold still until the next. It now uses a high percentile of recent arrivals,
  and shows movement continuing along its velocity for up to 0.12 s when a snapshot is late.
- **BufferMs** default 70 (configs still at the first default, 90, move to 70).
- **Protocol 2**: builds before this one are refused with "DIFFERENT OVERTIME 0.2.5 BUILDS: BOTH PLAYERS NEED THE
  NEWEST ZIP" instead of misreading the new events.
- **Prediction (decided against for now)**: it would make the client's own player move instantly but hits are judged on
  the host, so the timing gap for touches stays and looks worse ("I was under it"). The fix for touch timing is ball
  ownership by side (whoever's side the ball is on simulates it), a bigger rework, proposed as the next step.

### Step 28 — online lobbies (browser, codes, private lobbies, chat, up to 4 PCs)
- **Title ONLINE -> lobby browser** (`src/Online/LobbyBrowser.cs`): every Overtime lobby on Steam, worldwide (Steam lobby
  data marker + distance filter worldwide), with name, version, mode, status (LOBBY / IN MATCH) and PCs n/max; REFRESH,
  SAME VERSION and SHOW FULL filters; SEARCH BY CODE; CREATE LOBBY (name, PUBLIC on/off, 2-4 PCs); JOIN BY IP with an
  address field (saved to the config); BACK. Lobbies of other versions show red and can't be joined; full lobbies and
  lobbies in a match say so. Steam invites / "Join game" / `+connect_lobby` open the browser and join.
- **Lobby codes and private lobbies** (`Lobbies.cs`): each lobby gets a 6-character code (no 0/O/1/I) in its lobby data.
  A private lobby is a Steam "invisible" lobby with hidden=1: the browser list leaves it out, a code search finds it.
- **Lobby screen** (`VersusMenu`, lobby mode): the lobby's name, code, public/private and PCs; the seats (host runs them,
  every PC shows a copy); PLAYERS with each PC's ping, (YOU) and HOST; CHAT (T or click to type, Enter sends; while
  typing the key sets don't play); LEAVE, START (host), INVITE (STEAM). After a match the end screen's MENU reads LOBBY
  and takes everyone back to the lobby (the session stays, the lobby keeps its mode); the Steam lobby shows IN MATCH and
  is closed to joiners during a match.
- **Up to 4 PCs** (`Net.cs`, `Links.cs`): the host keeps a peer per friend (each with its own reliable channel on direct
  IP, or its own Steam session) and numbers them PC 2-4; device keys are PC x 10000 + the device, so each PC's keys and
  pads are told apart ("W A S D (NAME)"). The host waits for every PC to load each round; a PC that leaves or stops
  responding drops out of the session alone (its seats empty in the lobby; in a match its players pause with a notice
  and can be taken over). Protocol 3 (refuses older builds with a message).
- **Ping readout**: the host's corner lists every friend's ping; a friend's corner shows theirs.
- **Tested** (2026-10-12): one copy: title button, browser layout, typing a lobby name, CREATE LOBBY -> lobby screen with
  name/code/PCs, chat (typing "wasd" didn't take a seat; the Enter that sends no longer seats the arrow keys either).
  Three copies over direct IP: a 1v2 lobby for 3 PCs, both friends joined as PC 2 and PC 3 and took seats with their own
  WASD, a full 6-round match (4-5, 2-5 rounds), picks, LOBBY at the end -> all three back in the lobby (3/3 PCs, pings,
  chat kept); a friend killed mid-match -> host paused "P3 LOST THE CONNECTION", the other friend still connected and
  shown the notice. Steam (one copy under Steam): public lobby created, found in the list with all its data and by code.
  0 errors in every copy's log; saves unchanged.
- **Not testable alone**: joining a Steam lobby from another account (needs a second PC/account).


### Step 29 — fix: right-side spike aim and serve blocking (player reports, v0.3.1)
- **Spike aim on the right was backwards**: player 1 aims spikes by movement (the game's `velocityAim`), but the right
  team's spiker was made for the AI and aims by position, and the game's movement aim was never mirrored for the right.
  Now a person on the right (the spiker, and the setter in 1v2/2v2) gets movement aim, mirrored (BallMovement.Spike
  prefix/postfix flips the player's speed for that one hit): toward the net = long, away = short, like player 1.
  Checked in game (spike log): right still 120 deg / depth 1.73 = the mirror of the left's 240 deg; right toward the net
  depth 4.0 (long), away 1.4 (short); left away 1.3-1.6.
- **Player 2 could block every serve**: the game stops the left side from block-jumping for 0.7 s after the other side's
  serve but has no such rule for the right (always the AI there). Same rule for the right in Versus (UpInputPressed
  prefix: in that window the press is an ordinary jump). Checked with Ctrl+F10 (player 2 jumps right after each serve):
  "blocking False" every time; blocks later in a rally are unchanged.
- **Same serve line**: a person on the right now serves from player 1's line (COURT_SIZE 33.5; the AI teams' is 30).

## Feature batch (2026-10-20): known bugs first, then features 1-17 in order

### Bugs: jumping over the net, ball lost off the court, camera stuck zoomed in
- **Court geometry** (dev Shift+F9 dump): floor x -100..100 (top y -10); side walls |x| 70..80 up to y 88 (tagged Ground,
  so a ball hitting them is out); the net's invisible player wall x -0.5..0.5 only up to y 19.3; the ball already uses
  continuous collision (it doesn't tunnel).
- **Over the net**: boosted jumps (Jump cards, jump perks) reach well above y 19.3 and the player crossed onto the other
  side. Fix (`ControllerPatches.OwnSide`, PlayerController.FixedUpdate postfix): a player is held at the line the wall
  would stop them at (0.5 + their half width from the net). Normal play never reaches that line, so nothing else changes.
- **Ball lost / camera stuck**: one cause. A boosted ball (sky serve, lifts, speed-ups; reported on serves) clears a side
  wall (top y 88); past x 100 there is no floor, so it fell forever: the rally never ended, and the game's camera, which
  sizes itself from the ball's height, stayed fully zoomed in. Fix (`BallPatches.Arena`): a ball past the floor's ends,
  under the floor, above y 400 or with an invalid position for 0.25 s ends the rally as an out ball through the game's own
  point routine (point against the side that touched it last), host only online.
- **Reproduced and fixed in game** (local Versus, dev Ctrl+0 fixes off/on, Ctrl+1 launches player 1 at the net holding
  toward it, Ctrl+2 throws the ball over a side wall in free flight): fixes off, player 1 crossed to the other side and the
  ball fell to y -717 with the score frozen; turning the fix on scored that ball as out (it was at y -6127); fixes on,
  player 1 stayed at x -1.44 and the thrown ball was scored out at (109, 92) and play went on. 0 errors.

### Feature 1 — tutorial skip toggle (done)
- **Where**: the new **title bar** (top-left of the title screen, `src/Patches/TitleBar.cs`) -> **SETTINGS** -> SKIP
  TUTORIAL. The bar holds the mod's title-screen buttons (more come with later features) and hides while any mod menu
  or the Continue popup is open. Mod panels can now hold the title menu (`OvertimePanel.HoldTitle`) so clicks don't reach
  the title buttons behind them.
- **How**: `[General] SkipTutorial` in the mod's BepInEx config (never the game's save.json). The game decides at
  startup (GameManager.Awake) whether the tutorial plays; with the setting on, the mod's postfix turns it off. The pause
  menu's TUTORIAL button still plays it on purpose.
- **Tested**: dev flag `-ovt-forcetutorial` (DevKeys on) starts as if the tutorial had never been finished: skip off ->
  "Tutorial at launch: plays"; toggled on in SETTINGS (config written at once: `SkipTutorial = true`), restarted -> "Tutorial
  skipped (setting)". 0 errors. Gamepad navigation for the settings screen comes with feature 6.

### Feature 2 — update checker (done)
- `src/Patches/UpdateCheck.cs`: when the title screen first shows, one background request (UnityWebRequest, 8 s timeout)
  to GitHub's public API `repos/prxphet-ai/HangTime-Overtime/releases/latest`; only standard headers are sent. If its
  `tag_name` is a newer version than the installed one, a notice appears under the title bar ("OVERTIME vX IS OUT (YOU
  HAVE vY) · CLICK TO OPEN THE RELEASES PAGE"); clicking it opens the releases page in the browser. Any failure (offline,
  timeout, odd reply) logs one line and shows nothing. Startup never waits for it.
- Setting: `[General] CheckForUpdates` (default on), also SETTINGS -> CHECK FOR UPDATES.
- **Tested**: installed 0.3.1 vs latest v0.3.0 -> "up to date"; dev `-ovt-pretendversion 0.2.0` -> notice shown on the
  title; dev `-ovt-updateurl http://127.0.0.1:9/none` -> "no answer after 2.0 s ... nothing shown". 0 errors.
- Note: the check compares release tags, so publish releases with tags like `v0.3.1`.

### Feature 3 — report bug button (done)
- **Where**: title bar -> REPORT BUG, and the pause menu (REPORT BUG under TUTORIAL; cloned from the game's own pause
  button, added to its button group). `src/Patches/BugReport.cs`.
- **What it does**: a preview panel shows the whole report first: a template for the player's description ("What
  happened?", steps), mod version, game version (+ Unity), current mode (e.g. "Versus 1v1, round 2 (online host, 2 PCs)"),
  OS family and resolution, and the last 60 lines of the newest BepInEx log. **OPEN ON GITHUB** opens
  `github.com/prxphet-ai/HangTime-Overtime/issues/new` with title and body filled in (the player writes the description
  and submits; needs a GitHub account; long logs lose lines from the top to keep the link under ~7800 characters).
  **COPY** puts the same text on the clipboard (for people without GitHub). **CANCEL** / Esc closes it.
- **Privacy**: before anything is shown, the report is scrubbed: `C:\Users\<name>` paths -> `<user>`, this PC's Windows
  user name, the Steam name and everyone in the online session -> `<player>`, IPv4 addresses (with ports) -> `<ip>`
  (version numbers like "BepInEx 5.4.23.5" kept), Steam ids -> `<steam id>`, e-mail addresses -> `<email>`.
- **Tested**: title bar and pause menu both open it (pause: in a Versus match); the dev scrub test turned a line with the
  user path, names, a LAN IP with port, a Steam id and an e-mail into placeholders; COPY put 1640 characters on the
  clipboard without the Windows user name; the issue link is built (2605 characters). OPEN ON GITHUB not clicked in tests
  (it opens the browser). 0 errors.

### Feature 4 — win streaks (done)
- **Data**: `src/Core/Records.cs` writes `HangtimeOvertime_stats.json` next to the game's save (never save.json): per mode
  the current streak, best streak, wins and losses (arrays, since the game's JsonUtility only writes plain arrays from a
  mod's classes), plus Classic runs won, the highest Infinite round and Versus matches played (for the stats page in
  feature 7). Modes: Classic (and Loops), Co-op, Infinite, Versus (local), Versus (online); tournament modes will add
  their own keys.
- **Decision: a streak = matches won in a row in that mode**, kept across runs and sessions; a loss resets it.
- **Versus**: the side of this PC's own players counts (local: player 1's side; online: each PC records for its own
  players, so the host and the friend get opposite results; a PC with players on both sides records nothing). The
  match-end screen tells each PC the winner (online protocol 4).
- **Shown**: after a win, in the upgrade screen's GAME STATS card ("WIN STREAK 2 · BEST 2"); after a loss, a banner at
  the top of the lose screen ("STREAK ENDED AT 1 · BEST 2", or "BEST STREAK 2"); the Versus match-end screen under the
  score. The lose screen's stats text no longer wraps (a long team name pushed the mod's Infinite line into TRY AGAIN).
- **Tested**: Infinite win -> card line, loss -> banner (layout clear of TRY AGAIN); local Versus 5-0 for P1 -> "WIN
  STREAK 1 · BEST 1"; online match between two copies -> host "Versus (online) loss", friend "win, streak 1", friend chose
  LOBBY and both returned to the lobby. 0 errors. Test stats file deleted and saves restored afterwards.

### Feature 5 — key rebinding (done)
- **CONTROLS** (title bar) -> `src/Patches/ControlsMenu.cs`: tabs KEYS 1-4 (the four keyboard sets), PAD 1-4 (gamepad slots =
  the 1st-4th connected pad) and CHAT. Click a binding, press the new key / button (Esc cancels). A key already used
  elsewhere is accepted with a warning ("J IS ALSO USED FOR KEYS 3 · LEFT: BOTH WILL TRIGGER") and the row shows it in
  red; MENU OK / BACK may share buttons with play (never used at the same time). RESET TAB, RESET ALL.
- **Actions**: key sets: LEFT, RIGHT, JUMP / HIT, RECEIVE, MENU OK, QUICK CHAT, EMOTE (defaults: WASD+Space/Q/E;
  arrows+Enter/RShift/RCtrl; IJKL+H/U/O; numpad 4 6 8 5 + NumEnter/Num7/Num9). Pads: JUMP (A) + 2nd (RT), RECEIVE (B) +
  2nd (RB), MENU OK (A), BACK (B), QUICK CHAT (D-pad up), EMOTE (D-pad down), TYPED CHAT (Select). Typed chat key
  (keyboard): Enter. Saved in `HangtimeOvertime_controls.json` (`src/Core/Controls.cs`).
- **Where they apply**: Versus / online read them directly (a player plays with the set or pad slot they joined with;
  labels follow the keys, e.g. "G A J D"). Classic / Infinite: binding overrides on the game's own actions (key set 2
  replaces its arrow keys, key set 1 its WASD, pad slots 1/2 its gamepad buttons). Co-op: the game's keyboard split reads
  key set 2 (player 1) and key set 1 (player 2). **Decision**: the Versus gamepad receive button now matches the game's
  (B / RB instead of X / LT).
- **Dev virtual gamepad** (`src/Debug/DevPad.cs`, DevKeys + `-ovt-devpad <file>`): commands written to the file ("tap
  south", "hold rightTrigger 300", "stick -1 0 500", "wait 200") drive a virtual pad, for gamepad tests without a controller.
- **Tested**: rebound KEYS 1 jump -> G and receive -> J (conflict warning with KEYS 3 left), PAD 1 emote -> Y with the
  virtual pad; the game's actions showed `UpP3: <Keyboard>/g`, `DownP3: <Keyboard>/j`; Versus: joined with G ("G A J D")
  and with the virtual pad (GAMEPAD 1), player 2 walked with the pad's stick and jumped with A, player 1 jumped with G;
  bindings survived a restart; RESET ALL restored the defaults. 0 errors. Test bindings deleted afterwards.
- Quick chat / emote / typed chat actions are read already (PadState) and get used in feature 13.

### Feature 6 — gamepad support in the mod's menus (done)
- **Shared navigation** (`src/Patches/PanelNav.cs`, part of `OvertimePanel`): every Button / Toggle / TextField a panel
  makes is a stop. D-pad / left stick move a yellow frame to the next stop that way (in line first, then a 45° cone; up /
  down may fall back to anything below, so a tab row reaches the rows under it), A (the pad's MENU OK) activates, B (BACK)
  goes back, LB / RB switch tabs. Held directions repeat (0.35 s, then 0.12 s). Panels that don't share keys with players
  also take arrow keys + Enter. Prompt lines ("A: CHOOSE · B: BACK ...") only show while a gamepad was the last thing used.
- **On-screen keyboard** for text boxes (A on a text box with a pad): letters, digits, symbols, SPACE / DELETE / OK; X
  deletes, START finishes (and submits like Enter: a lobby name creates the lobby), B cancels. Enter on a keyboard still
  just types into the box.
- **Per menu**: title bar: SELECT puts the frame on SETTINGS / CONTROLS / REPORT BUG ("SELECT: THIS BAR" hint), B gives
  the title menu back. SETTINGS, CONTROLS (rebind with A, then the new button; START cancels, and B cancels a key
  capture), REPORT BUG, the lobby browser (LB / RB scroll the list), the versus / lobby screen (LB / RB change format, Y
  invites, SELECT opens the on-screen keyboard for chat, B leaves the seat then the screen). The title's VERSUS /
  CUSTOMIZE / ONLINE buttons are in the game's own button group (stick + A), CUSTOMIZE and the continue popup already had
  pad support.
- **Fix found while testing**: the game's `ButtonGroup` subscribes to the pad's A one frame after being enabled, from a
  coroutine that survives being disabled again; a panel that released and re-held the title menu in one frame (title bar
  -> CONTROLS) left the hidden title menu pressing CLASSIC. `ButtonGroup.Select` now does nothing while the group is
  disabled (hook `held_group_guard`).
- **Tested gamepad-only** (virtual pad, no keyboard or mouse): title bar -> CONTROLS, tabs with LB / RB, rebound PAD 1
  RECEIVE to Y then X (conflict line), RESET ALL, BACK; SETTINGS toggles both ways; REPORT BUG preview, move, cancel;
  title menu -> ONLINE, the on-screen keyboard typed "SDD", DELETE, START -> lobby "SD" created (direct IP, Steam off), B
  out; VERSUS: joined with A, team with the D-pad, RB to 1v2, ready, un-ready, leave, back. Keyboard / mouse after the
  change: arrows + Enter in the browser and settings, Enter on the lobby name typed "AB", Esc closed. 0 errors.

### Feature 7 — collection screen (done)
- **COLLECTION** (title bar, `src/Patches/CollectionMenu.cs`): tabs TEAMS / PERKS / STAT CARDS / STATS. A 5x7 grid per page
  (< > pages): found entries in their element colour with a rarity (or class) strip, the rest as ???. Filters cycle on
  click / A: RARITY (teams: CLASS regular / combo / boss; stat cards add GAME), ELEMENT (stat cards: STYLE boost /
  playstyle / tradeoff / game), SHOW all / found / locked, SORT name / rarity / element / most picked (teams: most faced
  / most beaten). The detail pane follows the highlight: title, rarity, element, rules text, stat changes or signature
  perks, flavour, OFFERED n · PICKED n (teams: FACED n · BEATEN n). Locked entries show only their rarity and how to find them.
- **STATS page**: wins, losses, win % and current / best streak per mode (Records), Classic runs won, highest Infinite
  match won, best Infinite run, most faced / beaten team, completion per category, most picked card.
- **What counts** (`src/Core/Collection.cs`, `HangtimeOvertime_collection.json`): a card shown on an upgrade screen is
  found, taking it is a pick (also Versus offers / picks); a team is found when a Classic / Co-op / Infinite match against
  it starts and beaten when that match is won (Versus teams are chosen, so they don't count). The lists: every sheet team
  + the game's 10, every sheet perk + the game's 24 techniques (names from `scaling.json` vanilla_rarity, card titles once
  seen), every sheet stat card + the game's own stat cards (catalogued from the first stat-card upgrade screen, a note says
  so until then). Nothing from before this version counts (no history was kept).
- **Tested**: empty collection (all ???); continued an Infinite run: Sunaumi High found at kick-off, F6 win -> beaten,
  stat screen found Dominator / Setter Sync / Fortress (7 game stat cards catalogued), picked Setter Sync; next match
  Hinotori Phoenix, perk screen found Afterburner / Radiant Guard, picked Afterburner. The screen showed 8 / 285 found,
  details, filters (SHOW: FOUND -> 3 cards), the stats page; mouse, keyboard and pad (LB / RB tabs, B back). Saves restored
  afterwards. 0 errors.

### Feature 8 — bracket screen between matches (done for Classic / Co-op / Loop; tournaments reuse it)
- **When**: Classic, Co-op and Loop open the bracket at the start of every upgrade screen (the screen is paused under it,
  its buttons and START-skip wait), and Classic shows it as **CHAMPIONS!** on the win screen. CONTINUE (A / Enter /
  Space / Esc / click) closes it.
- **What** (`src/Patches/BracketPanel.cs`, model `src/Core/Bracket.cs`): one column per round with the game's own match
  titles (QUALIFIER ... FINALS, remembered from its title card), YOU against each opponent; the winner gold, the loser
  struck through; BOSS / COMBO tags; lines between rounds light up as they're won. The YOU marker hops from the match just
  won to the next one, which then pulses. Classic's upcoming teams come from the game's own line-up (`TournamentLineUps`)
  and its fixed bosses; the combo match is drawn at kick-off, so it shows ??? until then (Loop draws every slot: ???).
- **Generic**: `BracketModel` is rounds of matches (A, B, winner, note) in plain strings and ints; when a round has half
  the matches of the one before, it's drawn as a knockout tree with elbow lines. Features 10, 11 and 16 (tournaments) pass
  their own model, and send it to other PCs as is. **Online sync** therefore lands with those tournaments: Classic and
  Co-op have no online version.
- **Panel kit fix found here**: panels were laid out in world units at whatever size the camera had, and copied the
  scene's first text object outside the title (the upgrade screen's animated, tilted dialogue). Every panel now scales
  itself to a fixed 36-unit layout height (the title's settled view; the camera starts zoomed in at 32) and keeps the
  title label's font, material and size; copied labels lose extra components and rotation. Title panels look as before;
  upgrade (view 32) and win (view 17) screens now match them.
- **Bug found here**: closing the champions bracket with A also pressed the win screen's LOOP 2 button, which appears after
  the bracket opens. While the bracket is open, `ButtonGroup.Select` and `ButtonController.Press` do nothing.
- **Tested**: continued a Classic run, F6 -> bracket "NEXT: FIRST ROUND · VS RENSHO", Namasito struck through, marker hop,
  A closed it and the upgrade screen carried on (stats, cards); F8 + F6 -> "NEXT: FINALS · VS SHIROGANE"; final won ->
  CHAMPIONS on the win screen, A closed it and the win screen stayed. Title panels (collection, versus) unchanged. Co-op
  takes the same path (RunMode.Normal) but wasn't run. Saves restored. 0 errors.

### Feature 9 — daily seeded Infinite runs (done)
- **DAILY RUN** (title bar, `src/Patches/DailyMenu.cs`): the UTC date, a countdown to the next one, today's state, START
  TODAY'S RUN (then PRACTICE RUN), CONTINUE a saved daily run, the last 8 days (first run, best, runs), best daily and days
  played. (A title-menu button sat behind the title screen's player, so it's in the bar.)
- **Rule chosen: the first run counts.** Every run that day is allowed; only the first sets the day's result, the rest
  only raise "best". Starting another run while the counting run is saved ends it with the wins it has (the menu says so).
  A daily run left unfinished can be continued after midnight and still counts for its own day.
- **What's seeded** (`src/Core/Daily.cs`, `src/Patches/DailyPatches.cs`): seed = FNV-1a of the date. Opponents: the day's
  whole sequence is computed from match 1 (no repeats within 3), so it doesn't depend on earlier play or on resuming.
  The kick-off (`GameManager.OnStart`: the game's opponent level-ups, the mod's opponent perks) and the upgrade screen's pool
  building run with Unity's random state seeded by day + match, then the old state comes back (rallies stay random).
  Offers: the first 2 unowned perks / 3 stat cards in the day's order for that match (weighted like the game's pool),
  handed to the card buttons as they appear. **Not identical for everyone**: offers skip what you own, and the mod's
  opponent scaling follows the player's power, so different picks give different later offers / opponent stats.
- **Saves**: a third run slot `daily` in HangtimeOvertime_runs.json (the normal Infinite run is never touched); results in
  `HangtimeOvertime_daily.json`; daily runs don't change the Infinite best or run count. The title card says "Daily -
  Match n".
- **Tested**: started today's run (seed 2624177733, counts): match 1 Hinode Sparks; F6 -> offers Tectonic Slam / Skyhook
  (log and screen agree), picked; match 2 Hoshikuzu Stargazers; quit; CONTINUE resumed at match 2 (still the daily slot);
  quit; PRACTICE RUN ended the first run at 1 win and started run 2: match 1 Hinode Sparks again, the same offers. The
  normal Infinite (round 6) and Classic saves were unchanged. Saves restored. 0 errors.

### Feature 10 — daily seeded tournaments (done)
- **Daily cup** = an 8-match Classic-style bracket seeded by the UTC date: DAILY RUN menu, **CUP** tab (LB / RB or click
  switches RUN / CUP). The tab shows today's whole lineup; START TODAY'S CUP, then PRACTICE CUP; CONTINUE a saved cup;
  history (first cup as "n / 8 WON" or CHAMPION, best, tries), best day and cups won.
- **How**: `RunState.StartCup` (presses the 1-player CLASSIC title button with `Daily.Pending` + `PendingCup`; no practice
  match, straight to match 1). `RunMode.Normal` + `RunState.Daily` = the cup (`Daily.Cup`). Opponents:
  `TeamRoster.CupPick` gives one seeded team per slot from that slot's pool (regular / boss / combo), no team twice, so
  the bracket screen and the menu show all 8 before they're played. Difficulty is Classic's (the game's own level-ups,
  seeded at kick-off; no mod scaling), so opponents are the same for everyone whatever their build. Offers and kick-offs
  are seeded as in feature 9 (cup seeds are offset from the run's). Title card "Daily Cup - <round>"; bracket title
  "DAILY CUP <date>". Run slot `cup`; results under "cup <date>" in HangtimeOvertime_daily.json.
- **Ends**: a lost match counts the matches won so far, the final counts 8 (champion bracket on the win screen). After
  that TRY AGAIN / LOOP 2 are normal runs.
- **Tested**: CUP tab lineup Raijin Tech > Fukurou Owls > Sunaumi High > Hinode Sparks > Kagaribi Flame > Oni Sakuen >
  Hinotori Phoenix > Iwagami Golems; started (counts): match 1 Raijin Tech; F6 -> bracket with the same names, offers
  Tectonic Slam / Lightning Reflex on screen and in the log; match 2 Fukurou Owls; quit, CONTINUE (MATCH 2) resumed it;
  F7 -> "over: 1 wins (counts)", save cleared. Practice cup with F8 + F6: final vs Iwagami Golems (as listed), won ->
  "over: 8 wins (practice)", CHAMPIONS bracket "DAILY CUP 2026-10-21". Saves restored. 0 errors.
- Fixed while testing: the champions bracket said CLASSIC (the cup flag is cleared when the win screen loads); the
  bracket now keeps the cup's day from kick-off.

### Feature 11 — draft tournament (done)
- **DRAFT** (title bar, `src/Patches/DraftMenu.cs`): three teams side by side, each with 3 abilities (rarity, element,
  rules text) and 2 stat cards, plus a strength bar from the simulator. PICK one: an 8-match **draft cup** (the daily
  cup's machinery with a random seed: one team per bracket slot, none twice, Classic difficulty) where every opponent has
  drafted too (3 extra perks, aimed at the perk part of a package's strength; "DRAFTED: ..." bubble). The drafted team is
  on the left banner and in the bracket ("DRAFT CUP · KAGE NINJAS"). The deal stays the same until a pick; CONTINUE
  resumes a saved draft cup (run slot `draft`). Records: drafts played, cups won, a Draft streak (stats page too).
- **Balance with the simulator** (`tools/draft.py`, `sheets/draft.json`):
  1. `power`: every Overtime perk and stat card alone on a fresh player, field win-rate gain over all 44 teams at round
     3 (n 150). Base 53.5%; perk median +6.5%, card median +8.1%.
  2. Summing powers wasn't enough: 12 drafts dealt to the same summed power (0.36) played 64-88%, worst spread
     inside a draft 22%. So `build` plays every candidate package and keeps the ones within 80% +- 2.5%: **102 packages,
     3 per team, 77.5-82.5%**. The game deals one package for each of three different teams.
  3. `check` replays 12 random drafts from the packages with new seeds: mean 80.8%, every option 78.0-83.0%, spread
     inside a draft median 2.1%, worst 3.9%.
  Re-run `power`, then `build`, after changing perks, cards or the simulator.
- **Online, where it makes sense**: Versus has **DRAFT START** (next to the mode tabs, host's choice): before round 1
  each side's first player picks one of three drafted teams on the round-screen overlay (synced to the other PC like the
  between-round picks); the team becomes the side's team (banner) and its build. The draft cup itself is single player.
- **Tested**: DRAFT screen (Hinode Sparks / Kage Ninjas / Fubuki Wolves, strength shown); picked Kage Ninjas: banner,
  opponent "drafted Quick Set, Lightning Rod, Second Breath", F6 -> bracket "DRAFT CUP · KAGE NINJAS", F7 -> draft loss,
  save cleared. Local 2v2 with DRAFT START: P1 drafted Tako Tentacles, P3 Hinotori Phoenix, the match loaded. Online
  (host + client on this PC): the client's player drafted Amaterasu Royals on their own PC, both screens showed the
  Hotaru / Amaterasu banners, rounds went on. 0 errors.
- Fixed while testing: a game team's in-match name can be shorter than its prefab ("Kozuki" vs "Kozuki Dan"): the cup
  lineup and the collection now use the right one each.

### Feature 12 — individual picks in co-op and team modes (done)
- **Rule** (`src/Engine/Ownership.cs`): a perk belongs to the player who picked it. Its triggers that are a character's
  own action (spike, serve, tip, set, dig, block, jump, block jump) fire only for that player's character; everything
  else (passives, streaks, the other team's spikes...), stat cards and the game's own techniques work for the whole team
  and are labelled **TEAM-WIDE**. A side with one human is unchanged.
- **Co-op** (`src/Patches/CoopPick.cs`): player 1 picks on the game's own upgrade screen (perk cards say "PLAYER 1 PICKS:
  THEIRS" or "TEAM-WIDE"), then player 2 gets an offer of their own of the same kind (never player 1's card) on a panel,
  chosen with key set 1, their gamepad or the mouse. The upgrade screen is paused until player 2 has picked.
- **Versus sides with two players** (1v2's right side, 2v2): the picker already alternated; now offers show TEAM-WIDE
  where it applies, a personal perk is the picker's, and the round screen lists builds per player: "P1: ...",
  "P3: ...", "TEAM: ...". Online the same screen goes to the other PC (it was already synced as text).
- **Tested**: co-op, two upgrade screens: player 2 offered three stat cards (took Setter Sync with D + W), then two
  perks (took Volcano Slam, "theirs"). The F9 log shows Skyhook (P1's) fires for P1's character and is skipped for
  P2's, and Volcano Slam the other way round, also in real touches. Local 2v2: round screens showed "P3: Volcano Slam,
  Ember Tip / P4: Iron Curtain / TEAM: Stamina War"; online 2v2: TEAM-WIDE labels and P2's own Showboat. 0 errors.
- **Balance note**: in co-op a personal perk now helps one character instead of both, while each player picks every
  screen, so the team gets twice the cards. Not run in the simulator (it models one player per team). Open question below.

### Feature 13 — quick chat wheel, emotes and typed chat (done)
- **Wheels** (`src/Patches/ChatHud.cs`, lines and emotes in `sheets/chat.json`): each player's QUICK CHAT / EMOTE key
  (key set 1: Q / E; see CONTROLS) or their gamepad's D-pad up / down opens a wheel of 8 next to their character.
  Keyboard: LEFT / RIGHT choose, JUMP sends, RECEIVE / Esc / the same key close (the other key switches wheels). Pad:
  the stick or D-pad points at a line, A sends, B closes. Local players can have wheels open at the same time.
- **Typed chat**: the CHAT key (Enter) opens a box at the bottom; Enter sends, Esc cancels (and doesn't pause). A
  gamepad's TYPED CHAT button (Select) opens the on-screen keyboard. 60 characters at most; long lines wrap in the bubble.
- **While chatting** that player's character takes no gameplay input (the three calls every player goes through), and
  typing keeps the key sets' keys out of play (the letters don't move anyone or open wheels). Online, a client's device
  sends nothing to the host while its player chats.
- **Showing**: a bubble above the character (pops in, fades; emotes are a big bouncing symbol), and a chat log in the
  bottom-left corner on a dark backdrop (6 lines, fading after 10 s; all lines while typing). Cooldowns: 1.2 s, typed
  2 s. A one-line hint of the keys shows at the first match of a session.
- **Online**: `Msg.MatchChat` (protocol 5). A client sends to the host; the host relays to every PC (the sender's too) with
  the sender's PC id; characters are named by MatchSync's player index (the same on every PC). Labels: "NAME (P2)".
- **Settings** (SETTINGS, stored in the BepInEx config `[Chat]`): CHAT BUBBLES (off: only the log), MUTE OTHERS' CHAT
  (online: other PCs' lines and emotes aren't shown).
- **Tested**: solo match by keyboard (wheel, Q + D + W -> "Mine!" bubble while the player stood still; emote; typed "hi
  there"; Enter + A + Esc: nothing sent, no pause) and by the virtual gamepad (stick right -> "Yours!", emote, Select ->
  on-screen keyboard -> "SS"). Local Versus: P1 (WASD) and P2 (IJKL) with wheels open together, both lines sent. Online
  (host + client on this PC, `-ovt-chat`): every line and emote on both PCs above the right characters and in both logs;
  mute on: each PC showed only its own; bubbles off: none. 0 errors.

### Feature 14 — lobby rules panel (done)
- **RULES** (next to the Versus / lobby screen's mode tabs, `src/Patches/RulesPanel.cs`, `src/Core/LobbyRules.cs`):
  points per round (1-15), rounds to win (1-9), upgrades per round (0-3; 0 = none), choices offered (2-4), perks on / off,
  stat cards on / off, rarity up to common / rare / epic, starting perks (0-5 random allowed perks per side), draft start,
  banned elements (one switch each) and banned perks (EDIT LIST: pages of perks by element). The line under the mode tabs
  sums them up ("RULES: QUICK MATCH · ROUNDS TO 3 POINTS · ...").
- **Presets**: `sheets/rules.json` (STANDARD = the old fixed rules, QUICK MATCH, MARATHON, PURE SKILL, STAT CARDS ONLY,
  COMMONS ONLY, CHAOS, DRAFT) plus three custom slots (SAVE AS CUSTOM 1-3), in `HangtimeOvertime_rules.json` with the
  rules last used (a new session starts with them). DRAFT START moved into the rules.
- **Online**: only the host edits; friends open RULES read-only. The rules go to every PC with the lobby screen (and
  each round load), a change un-readies everyone, and a friend's PC goes back to its own rules when the session ends.
  Protocol 6.
- **How they apply**: points -> the game's match length, rounds -> the target; offers skip what the rules forbid; with
  several upgrades per round the losing side picks again ("PICK 2 OF 2"; in 2v2 the partner takes the next pick); with no
  upgrades the round screen says so and the next round starts by itself.
- **Tested**: local 1v1 with QUICK MATCH + 2 upgrades: round won 3-0, "FIRST TO 3 ROUNDS", P2 took two picks, round 2
  loaded. PURE SKILL: "NO UPGRADES THIS MATCH", next round by itself. Rules kept after a restart. Online (`-ovt-rules
  chaos`, dev flag): the client logged the host's CHAOS rules, both sides started with 3 perks. The friend's read-only
  view wasn't clicked (two copies of the game on one PC can't both take the mouse). 0 errors.

### Feature 15 — replays (done)
- **Determinism, checked first**: the game isn't deterministic. Rallies run on Unity physics (`Rigidbody2D`, frame
  timing), the AI and the perks roll `Random` constantly, and the camera / time-scale tricks change the step. A replay
  of inputs + seed would drift apart within seconds. **Chosen: the snapshot fallback**, which already existed: the online
  host's match stream (50 snapshots a second of the ball and the four players with their animation states, plus timed
  events for sounds, camera, ball effects, perk visuals, voices, lines, jumps and points).
- **Recording** (`src/Online/Replays.cs`): every Classic / Co-op / Infinite / Loop / daily / draft match and every
  Versus round. Local games start the same recorder the online host uses; an online friend keeps the stream as it
  arrives. The header carries teams, look, gym, points, title, date and final score. On the scene's end the replay goes
  to `HangtimeOvertime_replays/recent-*.ovr` (gzip, about 100-130 KB a round); the last 10 are kept, shorter than 3 s
  dropped. SETTINGS > RECORD REPLAYS (`[Replays] Record`).
- **REPLAYS** (title bar, `src/Patches/ReplayMenu.cs`): recent and saved replays with title, date, length and score;
  WATCH, SAVE (keeps a recent one for good: `saved-*.ovr`), DELETE (saved ones).
- **Viewer**: the replay's scene loads like a Versus round that only mirrors (the friend's-PC mirror, on a replay clock).
  Controls: play / pause (Space / A), speed 0.25x-4x ([ ] / LB RB), seek 5 s (arrows / D-pad) or click the time bar,
  camera GAME / WHOLE COURT / FOLLOW BALL (C / Y), restart (R / Select), exit (Esc / B). Seeking sets the score from the
  snapshot there and skips the events in between.
- **Tested**: Classic match recorded (43 s, 99 KB), watched (players moving as played; seek to the end -> final score;
  4x; both camera modes; restart); local Versus round with rallies recorded and watched (score following the points,
  the opponent's line replayed); SAVE; Esc back to the title with the saved runs intact. Online: host and friend both kept
  the round (2207 messages each). 0 errors. Test replays deleted afterwards.
- **Limits**: a replay shows what was on the host's screen; perk visuals that aren't in the stream (some screen-space
  effects) don't replay. Playback isn't offered while online (leaving a replay ends the Versus state, which would end a
  session).

### Feature 16 — party tournament (done)
- **PARTY CUP** (the Versus / lobby screen's left button, `src/Patches/PartyMenu.cs`, `src/Core/Party.cs`): players join
  one by one (JUMP on a key set, A on a gamepad, a friend's device online), RECEIVE / B leaves; bracket of 4 or 8; the
  AI fills the open places (named after teams); RULES opens the lobby rules (every match uses them). START.
- **The cup**: single elimination, players spread across the bracket so they don't meet in round 1. Each match is a
  1v1 Versus match (each entrant with a sheet team). Two AI entrants settle their match at once (a coin weighted by the
  teams' strength offsets); a player against the AI plays the game's own AI team on the right (the AI side makes its own
  upgrade picks). Between matches the bracket screen (knockout tree, NEXT marker, results), then CHAMPION and back to
  the Versus screen / lobby. Builds don't carry from match to match.
- **Online**: the host runs it; entrants come from every PC. **Spectating while waiting**: the PCs not playing a match
  watch it live (the normal mirror) and get the bracket between matches (`Msg.PartyBracket`, protocol 7; the side names
  travel with each round load).
- **Disconnects (chosen)**: a player whose PC drops out forfeits the match in progress, and the AI plays their later
  matches under their name ("... (AI)") so the bracket keeps its shape.
- **Tested**: local cup of 4 (P1, P2 + 2 AI) with 1-round matches: brackets, both semi-finals, the AI final settled,
  CHAMPION, back to the menu; the player moves and plays against the AI side. Online (`-ovt-party`, dev): the friend's
  device joined, the friend's PC watched match 1 and got each bracket; killing the friend's game during their match ->
  forfeit, "(AI)", the cup finished. 0 errors.
- The dev autopilot loses to the game's AI teams (it was written for player-vs-player); that's the harness, not the cup.

### Feature 17 — spectators (done)
- **Joining as a spectator**: a friend who joins while a match is running is no longer refused: they come in as a
  spectator (the host's roster says "(WATCHING)") and see the match from its next round. In the lobby, a friend's PC
  has **WATCH ONLY** (where the host has PARTY CUP): on, its devices can't take a seat (the host frees any it had); off,
  it plays again. A spectator never holds a round up (the host doesn't wait for its loading).
- **What a spectator sees** (`src/Online/Spectator.cs`): the match through the normal mirror, a bar with both sides,
  round wins, the points and the target; C / Y cycles the camera (GAME, WHOLE COURT, FOLLOW BALL: the same
  `CameraModes` as the replay viewer); B / X shows both builds (from the host's last between-rounds screen); Tab /
  Select shows the party cup's bracket (the bracket also shows by itself between party matches).
- **Chat and emotes only**: a spectator's quick chat, emotes and typed lines go to every PC's chat log labelled
  "NAME (WATCHING)", with no bubble (no character). Its devices can't touch the match.
- **Tested**: three copies on this PC (host + player + spectator joining mid-match, protocol 8): the spectator was let
  in as PC 3, mirrored round 2 with the score bar, its emote and typed line reached the host's log, the host's rounds
  started "every PC ready" without waiting for it; the camera views and the builds panel stepped through (dev flag
  `-ovt-watchdemo`). The WATCH ONLY switch wasn't clicked (three copies can't share the mouse); it sends the same
  message the join path uses. 0 errors.

### Co-op Infinite, local and online (v0.4.1)
- **Local** (the game's own flow): the title's CO-OP now asks CLASSIC or INFINITE. Co-op Infinite is Infinite with the
  game's co-op pair: the same scaling opponents, the upgrade screen with individual picks (player 1 on the game's screen,
  then player 2's own offer), saved in its own run slot (CONTINUE / NEW RUN; player count and who owns which perk are
  saved), title card "Co-op Infinite - Match n", its own streaks and best run (stats page). Solo Infinite's best and
  run count aren't touched.
- **Online** (a lobby mode, `VersusSetup` format `coop`): the lobby's 5th tab **CO-OP INFINITE**. Two players on the left
  (any PCs: the host's P1 and a friend's P2, or both on one PC) against Infinite's opponents on the right, run on the
  Versus online system (host plays, friends mirror; spectators, chat and replays work). The host picks each match's
  opponent before the round load (`VersusState.PickCoopOpponent`, same pools and bosses every 8 as Infinite) and scales
  it with `OpponentScaling` (round = match number). The game's own match length. After a win each player picks an
  upgrade on the round screen (synced; personal perks are the picker's, the rest team-wide); one loss ends the run: RUN
  OVER with the best co-op run, TRY AGAIN (a new run) or LOBBY. Lobby rules don't apply here except which perks / cards
  can be offered. Protocol 9.
- **Tested**: local: CO-OP > INFINITE started with both players, F6 -> upgrade screen, player 2 took Tidal Wall
  (team-wide), quit, CO-OP > INFINITE > CONTINUE restored round 2 with 2 players and the owners (Splash Block = P1),
  F7 -> run over, best recorded, slot cleared. Online (host + friend on this PC, `-ovt-format coop`, dev `-ovt-winfirst
  1`): match 1 on both PCs, won -> "MATCH 1 WON!", P1 then the friend's P2 picked, match 2 against a new team on both,
  lost -> RUN OVER on both, TRY AGAIN -> a new run at match 1, LOBBY -> both back in the lobby. Online 1v1 regression:
  rounds and picks fine. 0 errors.

## Leaderboards and community tournaments (2026-10-23)

### 0. Backend decision — needs your free Cloudflare account (setup steps in `server/SETUP.md`)
- **Chosen: Cloudflare Workers + D1** (`server/`). Free tier: 100,000 requests a day, 5 GB, no card; at the limit it stops
  for the day instead of charging. One small JavaScript file (`server/worker.js`) holds every rule, so the server, not the
  game, decides what is valid. Other options looked at: Supabase / Firebase (free, but their row rules can't run the
  run-log checks, and a public key there is a write key to the tables), a VPS (costs money), Steam leaderboards (need the
  game's own Steam app; a mod can't add boards to it).
- **Every option needs an account you create**, so per the plan the online part is built and tested only against a local
  copy (`node server/dev_server.mjs`, the same worker on a SQLite file) and is **not deployed**. The game's default
  server address is empty: until it's set, LEADERBOARDS says "NOT CONNECTED: THE COMMUNITY SERVER ISN'T SET UP YET"
  and finished runs queue up locally.
- **What you do** (about 10 minutes, `server/SETUP.md`): Cloudflare sign-up, `npx wrangler login`, `wrangler d1 create`,
  paste the database id into `server/wrangler.toml`, `wrangler d1 execute ... --file=schema.sql`, `wrangler deploy`, then
  give me the `https://....workers.dev` address to make it the default in `Plugin.cs`.
- **No secret keys anywhere**: the game carries no key. Each player's game makes a random 64-hex secret
  (`HangtimeOvertime_identity.json` next to the saves, never in the repo or the queue file); the player id is the first 20
  hex of its SHA-256, and the server stores only the id. The database id in `wrangler.toml` isn't a secret (it only works
  with your Cloudflare login). `server/*.sqlite` and `.wrangler/` are git-ignored.
- `sheets/community.json` (limits + filtered words) -> `Generated.Community` and `server/generated.js` (the same numbers
  and the perk / card ids for the server) from `python tools/sheets.py gen`.

### 1. Leaderboards (done; online once the server is up)
- **Boards**: CLASSIC SPEEDRUN (fastest Classic clear, solo, normal difficulty) and INFINITE (most rounds won in one solo
  Infinite run). One set of boards per mod version line: v0.4.x results never mix with v0.5.x (balance changes between
  them). Each player keeps their best per board.
- **What's timed**: real time from the run's start (the first match's loading) to the final point of match 8, counting
  every match, upgrade screen, bracket and loading in between; **not** the pause menu or the title screen; slow motion and
  game-speed perks don't change it (real time). A freeze or alt-tab counts at most 1 s per frame. Saved with the run, so
  CONTINUE keeps the time (quitting to the title stops it). RESTART starts again at 0.
- **Screen** (title bar LEADERBOARDS, under the other buttons): tabs CLASSIC SPEEDRUN / INFINITE (click, or LB / RB), the
  board's version line and player count, a table of rank / player / result / date / version (top 50, pages of 12 with
  < >), your own row highlighted, "YOU: #rank · best · date" even when you're outside the top 50, your name box + SAVE
  NAME, the last result's status ("NEW BEST: #3", "QUEUED (OFFLINE)", "NOT SUBMITTED: DEV KEYS ON"...), how many results
  are waiting, REFRESH / BACK. Gamepad: d-pad / stick between controls, A, B back, LB / RB tabs, the on-screen keyboard
  for the name.
- **Name**: your Steam name by default (if it passes the rules), or one you type (2-16 characters, no `<` `>`, the
  word filter), changeable any time; the server keeps the newest name for all your scores.
- **Submitting**: automatic at the end of a qualifying run (a line at the top of the screen shows the result); Settings
  has **SUBMIT TO LEADERBOARDS** (on by default) to opt out. Without a name yet, the result waits until you set one.
  Offline or server down: queued in `HangtimeOvertime_pending.json` (up to 20) and sent on the next launch / when the
  leaderboard opens.
- **Not submitted** (the run is "modified"): dev keys on, debug logging on, Infinite difficulty changed, any `-ovt-`
  development launch option, a daily run / cup, draft, co-op, a run continued from an older mod version. Modified runs
  don't submit at all (there's no separate board for them).
- **Cheat checks** (server side, `checkScore`): Classic: 4 min to 6 h, a run log with 8 won matches in slots 1-8, each at
  least 20 s, adding up to no more than the time; Infinite: 1-500 rounds, a log with exactly that many wins, at least 25 s
  per round. Rate limits: 20 scores a player an hour, 300 requests an IP an hour. Every score keeps its run log
  (`slot,points,opponent points,seconds,opponent` per match) and seed for you to inspect; `flagged = 1` hides one
  (`server/SETUP.md`). This stops edited requests with impossible results, not a careful cheater who fakes a believable
  log: anything the game can send, a modified game can too.
- **Code**: `src/Online/Community.cs` (Identity, HTTP), `src/Online/Leaderboards.cs` (queue, submit, boards),
  `src/Core/RunClock.cs`, `src/Patches/LeaderboardMenu.cs` (+ `Toast`), `server/` (worker, schema, local dev server).
- **Tested** (local server on 127.0.0.1, dev launch `-ovt-boardtest` which lets a dev-keys run submit): an Infinite run
  submitted and ranked, a worse one kept the best; no name -> queued -> SAVE NAME -> sent; a real Classic clear (driven,
  10:11.8, 9-match log) accepted with rank 1; a run with dev keys and no `-ovt-boardtest` refused ("DEV KEYS ON");
  server stopped -> "QUEUED (OFFLINE)" -> relaunch with the server up -> sent; board tabs, pages, name box and buttons by
  keyboard and by the virtual pad; a second identity (`-ovt-profile B`) showed both players with the right row
  highlighted. Server rules by script (`node`): short / long times, missing or inconsistent logs, too many rounds, bad
  names and filtered words, rate limits, all refused with the right message. 0 errors in the game log.

### 2. Tournament creator and share codes (done; works offline)
- **Where**: title bar **TOURNAMENTS** -> this PC's tournaments (made here or added from a code), each with your record
  (attempts, clears, fastest). NEW TOURNAMENT opens the editor; ADD CODE (PASTE) reads a code from the clipboard (a
  button, so a pad can do it; Ctrl+V works too). Per tournament: PLAY / TEST PLAY, CONTINUE (a saved run), EDIT (your
  own), COPY CODE, DELETE (press twice).
- **Editor, tournament page**: name (32), a line about it (160), **TYPE** FIXED LOADOUT or NORMAL DRAFT, **MATCHES** 1-8
  (the last is the final), the match list (each opponent with the simulator's "YOU WIN ~n%"), **YOUR TEAM** (FIXED), the
  **difficulty** 1-10 with the estimated clear chance; SAVE, TEST PLAY, COPY CODE, CLOSE (saves). New tournaments start
  as "MY TOURNAMENT n" with three of the mod's teams to edit.
- **A match's page**: team name (20) and kick-off line (60), **SQUAD** (one of the ten vanilla teams: its body, AI and stat
  tables), **COPY LOOK** from any of the mod's 34 teams (colours + emblem), JERSEY / SHORTS / HAIR 1 / HAIR 2 / BANNER
  (a palette of the mod's team colours), the emblem builder (**MASCOT** from the 34 emblem mascots, **LAYOUT** top /
  stacked / split, **LETTERING** bangers / marker, **ACCENT** colour) with a live preview on the banner cloth, **stat levels**
  0-3 for the seven stats, up to 6 **perks** and 6 **stat cards** (only the mod's own: 154 perks, 47 cards), < EARLIER /
  LATER > to reorder, REMOVE THIS MATCH. LB / RB go to the other matches.
- **YOUR TEAM** (FIXED): the player's seven stat levels 0-3, up to 8 perks and 8 stat cards.
- **Picker** (ADD PERK / ADD CARD): every perk by element (cards by rarity), pages of 24, the pointed-at or focused one
  described at the bottom with its simulator value.
- **Emblem builder** (`src/Engine/EmblemArt.cs`): the same pictures as the mod's team emblems, composed in game from pieces
  `tools/emblems.py parts` draws (`assets/emblems/parts`: each mascot alone with its accent parts black, recoloured in game;
  a letter atlas per lettering font; deployed and in the zip). The layouts match `tools/emblems.py` (checked side by side
  against the shipped Fukurou emblem: the same picture). Lettering covers A-Z, 0-9 and `!?&'.-#+:`; other letters are left
  out of the emblem (the name itself keeps them; the editor says so).
- **Difficulty** (the simulator option, not a budget): `tools/tournament_rating.py fit` plays 3000 random custom teams
  (random squad, levels, perks, cards) against random player loadouts in the match simulator (40 matches each) and fits
  one logistic formula: the player's win chance per match from the squad, the summed stat levels and the summed perk /
  card powers (`sheets/draft.json`, each perk / card's simulated win-rate gain) on both sides. The clear chance is the
  product over the matches; difficulty = 1 + 4.5 x -log10(clear chance), 1-10 (about 1 in 100 = 10). DRAFT counts one
  pick per match won (a good offered pick's power, perk and card in turn). `check` on 400 fresh matchups x 200 matches:
  mean error 0.13, well calibrated by band (predicted 0.4-0.6 -> simulated 0.49; 0.6-0.8 -> 0.72; 0.8-1.0 -> 0.83). Per-stat
  levels and perk counts as extra inputs only took the error from 0.140 to 0.128, so the simple form stays. Coefficients:
  `sheets/community.json` `rating` -> the game (`Generated.Community.Rating*`) and the server (`server/generated.js`), which
  computes a published tournament's difficulty itself.
- **Share codes**: `OVT1.<content>.<clear record>.<check>`: the tournament as JSON, deflated, base64url; the creator's
  clear record (hash of exactly this content, the match log, the time) the same way, or empty; the first 8 hex of the
  SHA-256 of the rest. Checked on paste: the checksum ("damaged or incomplete"), the format number ("made with a newer
  Overtime: update the mod"), then every rule the server also checks (names and the word filter, squads, colours, emblem,
  levels 0-3, known perks / cards and the limits). A 1-match code is about 500 characters (the server's limit is 12000).
  The same content gives the same id (hash) on every PC, so adding a code twice keeps one entry and its record.
- **Beat it before publishing**: a clear of your own tournament (any play of it; TEST PLAY says so) stores the clear
  record in it ("READY TO PUBLISH"); any edit is a new version and needs a new clear; a run with dev keys / debug options
  doesn't count. Codes can be copied before that, marked "NOT CLEARED BY ITS MAKER YET"; publishing (section 4) needs it.
- **Playing one** (details in section 3): the game's own run from slot 9 - N to the final, each opponent a custom team with
  exactly its levels, perks and cards (`src/Patches/TournamentRun.cs`); its own run slot and record; no Classic boards,
  Classic stats or Steam achievements.
- **Tested** (keyboard, mouse and the virtual pad, one copy of the game): made a tournament, renamed it, edited a team
  (squad, colours, emblem, perks via the picker), saw the win chance move (72% -> 54% after adding a perk); TEST PLAY of a
  1-match FIXED tournament -> custom banner on the gym wall, the team's perks only, cleared -> "READY TO PUBLISH"; COPY CODE
  -> decoded by the server's own code in node: same hash, every check passes, the clear record verifies, the same
  difficulty (2); ADD CODE with a damaged code, a non-code and the same code again -> the right messages; a 3-match FIXED run:
  bracket between matches, no picks, the next match loads; closed the game mid-match -> CONTINUE -> cleared with a 3-match
  log and the clock carried over. Pad: title bar -> TOURNAMENTS -> NEW -> a team page, left / right change a row, the picker
  (RB pages, A adds), the on-screen keyboard on a text field. Simulator parity check 0 mismatches; the card refactor in
  `tools/simlib.py` gives identical results. No online part in this section (codes are copy / paste).
- **Found and fixed while testing**: Unity's JsonUtility dropped the nested teams (codes had 0 matches): tournaments now use
  a small JSON reader / writer (`src/Core/MiniJson.cs`). A menu opening another in the same frame handed the title menu
  back (the old menu's cleanup ran after the new one held it), so a pad A on the editor started the saved Classic run
  underneath: the title hold is now shared by all menus (this affected any menu-to-menu switch, not only these). The
  MATCHES row couldn't be reached by keys / pad and its arrows didn't redraw: value rows are now one stop that left / right
  change.

### 3. Playing a community tournament (done)
- **The run**: the game's own Classic machinery from slot 9 - N up to the final (slot 8), so the last match is the game's
  final with its gym, its win screen and CHAMPIONS; a 3-match tournament plays slots 6, 7, 8. Each opponent is spawned on
  its squad's body and gets exactly the creator's stat levels (the squad's own stat tables at those levels, no round
  scaling), perks (the squad's built-in ones are removed) and stat cards (as card points on the right team). Banner: the
  emblem from the builder on the gym wall in the team's banner colour; jersey, shorts and hair dressed like the mod's teams.
- **FIXED**: the player starts with the creator's levels, perks and cards and gets nothing new: after a win the upgrade
  screen shows the recap and the bracket, its buttons are hidden, and the next match loads when the bracket is closed (the
  pad's START skip is blocked so it can't load twice). **DRAFT**: the player starts with nothing and the normal upgrade
  screens (perks / stat cards) come after every win, against the custom teams.
- **Bracket** between matches: one column per match (MATCH 1 ... FINAL), each opponent with its emblem, name, and how many
  perks / cards it has; "NEXT: VS TEAM · its first perks". CHAMPIONS on the win screen with the time.
- **Opponents announce their abilities** at kick-off ("Our abilities: Frost Serve!", up to four, then "+n more"), and say
  the creator's kick-off line as their intro.
- **Record** per tournament (this PC): attempts (every start and TRY AGAIN), clears, fastest clear (the run clock: real
  time, pauses don't count). Shown in the list and the detail. A loss ends the run; the lose screen's TRY AGAIN starts the
  same tournament again from match 1 (a new attempt, loadout reset).
- **Run save**: its own slot (`community`, with the tournament's hash), saved at each kick-off, upgrade screen and pick like
  the other runs; TOURNAMENTS -> the tournament -> CONTINUE (MATCH k/N) or PLAY FROM THE START. A saved run whose
  tournament was deleted can't be continued (deleting a tournament also drops its saved run).
- **Kept apart from Classic**: no Classic speedrun submission, no Classic stats or "runs won", its own win-streak line
  ("Community"), no LOOP 2 button on its win screen, no Steam achievements (a 1-match tournament is the game's final).
- **Tested** (one copy, keyboard / mouse / virtual pad): FIXED 3 matches with the bracket and no picks (section 2); DRAFT 3
  matches: won match 1 -> bracket with the three emblems -> the game's recap -> two perk cards offered -> took Cannonball
  Serve -> match 2; lost it -> "Knocked Out" -> TRY AGAIN -> match 1 again (attempt 2) -> won -> pick -> match 2 started
  with "Our abilities: Frost Serve!". Closed mid-run and continued (section 2). 0 errors in the log.
- Not online: tournaments are single-player runs; nothing here talks to other PCs except the browser (section 4).

### 4. Tournament browser (done; online once the server is up)
- **TOURNAMENTS** has two tabs (click, or LB / RB): MY TOURNAMENTS and **BROWSE ONLINE**.
- **Publishing** (MY TOURNAMENTS, your own): PUBLISH ONLINE once you've cleared it (otherwise "BEAT IT FIRST"); the server
  checks the code again, the clear record (every match won, at least 20 s each, for exactly this content) and the word
  filter, computes the difficulty itself, and allows 5 new tournaments a player a day. The same content published twice
  is one tournament. After an edit (and a new clear): UPDATE ONLINE (same id; its plays, clears and fastest clears start
  again, its ratings stay). REMOVE ONLINE (press twice) takes it down for everyone and keeps it in your list. Deleting it
  from your list doesn't take it down (the status says so).
- **Browsing**: the published tournaments, 6 a screen, more pages fetched as you go; each row: name, creator, the mod
  version it was made with, FIXED / DRAFT, matches, difficulty, players, clear rate, rating. SORT (newest, most played, top
  rated, hardest, easiest), TYPE (all / fixed / draft), MINE, REFRESH. The detail adds the description, every match (with
  the simulator's win chance) and the **fastest clears** (top 4 and your rank). PLAY (adds it to your list and starts it),
  ADD TO MY LIST, **RATE IT 1-5** (after playing it; one rating a player, changeable), **REPORT** (press twice; three
  players' reports hide it until you look at it, `server/SETUP.md`). Your own published ones show REMOVE ONLINE instead.
- **Results**: every finished attempt at a downloaded (or your published) tournament is sent (cleared or not, the time and
  the run log) through the leaderboards' offline queue; the server counts players, clears and keeps each player's
  fastest clear. Refused: a run log that doesn't show every match won in at least 20 s, an impossible time, a result for an
  older version ("this tournament was updated since you got it"), modified runs (not sent at all), more than 60 results a
  player an hour.
- **Server additions**: the list returns the mod version; an update resets that tournament's plays / clears / fastest
  clears; results carry the content hash. All in `server/worker.js` (no new tables).
- **Tested** with the local server and two identities (`-ovt-profile A` / `B`, run one after the other: two windows can't
  share the input driver): A published its cleared 3-match tournament -> listed with "BY TESTERA · v0.4.1 · FIXED · 3
  MATCHES · DIFFICULTY 5/10"; B browsed, PLAY added and started it, cleared it in 1:49.5 -> the server had 1 player, 1
  clear, B first on the fastest clears; B rated it 4 (server 4.0 from 1 rating) and reported it (once: still listed); A
  removed it online from its list. By script against the same server: update by someone else refused, update without a
  clear record refused, the creator's update reset plays and fastest clears and kept the rating, a result for the old
  version refused, an impossible run log refused, rating without playing refused, a filtered tournament name and team name
  refused, an unknown perk refused, a damaged code refused, three reports hid a tournament, delete by someone else refused,
  delete by the creator worked, sort / type filters. The pad switches the tabs (RB / LB). Leaderboard server tests re-run:
  unchanged.
- **Limits**: a clear record proves only that the content was beaten plausibly by a client that follows the rules; a
  rewritten client could forge one (as with leaderboard results). Reports only hide; removing is up to you (SETUP.md).
- **Found and fixed while testing**: the tab buttons both opened MY TOURNAMENTS (a loop variable captured by the click);
  with `-ovt-profile` the tournament list and result queue now get their own files too (two test identities on one PC).

## UI overhaul (2026-10-24)
- **Look**: every menu in the style of the approved mockups and the tournaments screen: hand-drawn panel shapes with a
  marker outline and a drop shadow (`tools/ui_art.py` -> `assets/ui`: three panel variants, sliced, and 24 icons), the
  navy / blue / gold / orange palette, a gold title ribbon, folder tabs, cards, badges, gold "main action" signs, icon
  buttons, a pulsing white-gold focus ring for keys / pads, and Bangers lettering (SIL OFL, shipped as `ui/Bangers-Regular.ttf`
  with its licence, made into a TextMeshPro font at runtime).
- **Logo A**: the game's own HangTime! logo kept, moved over the gap between the VERSUS column and CLASSIC; under it OVERTIME
  on a folded orange ribbon with a stopwatch and the credit on a dark tag (`tools/title_logo.py`). The title menu moved down.
- **Title bar**: PLAY / COMMUNITY / GAME groups of icon tiles, a line that asks the community server whether it's there.
- **The game's own buttons** (title menu, pause, lose, win, upgrade screen, and the mod's buttons cloned from them) are
  painted signs (`SignSkin`): same buttons, same hover and animations; CLASSIC and the "go again" buttons gold.
- **Rebuilt screens**: tournaments (cards with emblems and flame meters, a poster with the match ladder), settings (toggle
  cards), leaderboards (medals, a YOU panel), customize (stage with the live player, option rows), draft cup (team cards with
  emblems), Versus and the lobby, the round / draft-pick overlay, the CO-OP and saved-run popups, daily, collection, replays,
  controls, report bug, the lobby browser, the tournament editor and picker, rules, party cup, the bracket.
- **Why colours had washed out**: the game tone-maps and lights its picture (a 50% grey showed at 73%). The menus are now
  drawn by their own overlay camera without post-processing (`UiCamera`, an unused layer the main camera stops drawing),
  with URP's unlit sprite shader; measured with a dev colour card (`-ovt-colortest`): every swatch shows its exact value.
- **Fixed on the way**: the leaderboards / daily screen flashed the title for a frame when switching tabs (menu objects were
  made on the normal layer for a frame): they go on the menus' layer as they're made (180 recorded frames of tab switches:
  none bright). The title camera could stay centred on the court after a rally ended on the title screen: it keeps the menu
  framing. The customize screen let the title signs show through (it wasn't a kit panel): rebuilt on the kit. A menu opening
  another in the same frame could hand the title menu back (one shared hold).
- **Dev helpers** (dev keys only): `-ovt-open NAME` opens a menu for screenshots (creator, versus, coop, continue, lobby,
  browser, party, rules, bracket, editor, or a title-bar label), `-ovt-colortest`, `-ovt-uidebug`, `-ovt-nosigns`.
- **Tested**: every screen above captured in the game at 1600 x 900 (keyboard and mouse; the virtual pad for the title
  menu and customize); the leaderboard filled from a local test server; a Versus draft pick and an Infinite match, pause and
  upgrade screen; mouse clicks on painted game buttons checked with and without the signs. Not captured: the win and lose
  screens after the change (same painted buttons as the pause menu) and online with two PCs.
## Session status (2026-10-24): leaderboards and community tournaments, v0.5.0
- **Finished** (each built, deployed, tested and committed; details in sections 0-4 above):
  0. Backend chosen and built: Cloudflare Workers + D1 (`server/`), no keys in the game or the repo, a local copy for
     testing (`node server/dev_server.mjs`).
  1. Leaderboards: CLASSIC SPEEDRUN and INFINITE per mod version line, names, auto-submit with an opt-out, offline queue,
     run log checks and rate limits, gamepad.
  2. Tournament creator: custom teams with the emblem builder, perks, stat cards and stat levels, FIXED / NORMAL DRAFT,
     simulator difficulty, share codes, test play and the clear record.
  3. Playing: FIXED / DRAFT runs, bracket with emblems, ability announcements, record per tournament, run saves, retry.
  4. Browser: publish / update / remove, sort and filter, players, clear rate, ratings, fastest clears, reports.
  Also: version 0.5.0 (the leaderboards use v0.5 boards; the online match protocol is unchanged at 9), README.
- **Waiting on you**:
  1. **The community server** (about 10 minutes, free, no card): follow `server/SETUP.md` (Cloudflare account, `npx
     wrangler login`, create the database, paste its id into `server/wrangler.toml`, create the tables, deploy). Then give
     me the `https://....workers.dev` address and I'll make it the default `[Community] Server` in `src/Plugin.cs`. Until
     then leaderboards say NOT CONNECTED (results wait on each PC) and BROWSE ONLINE is off; share codes work without it.
  2. Pushing: v0.4.1 (co-op Infinite) and everything since are committed locally, not pushed; nothing released.
- **Not tested with real people / the real server**: the online parts ran against the local copy of the same worker on
  this PC with two test identities one after the other (not two PCs, not Cloudflare). After you deploy, a quick check from
  two PCs (submit a run, publish and play a tournament) is the next step.
- **Open questions**:
  1. Classic speedrun timing: real time from the first match's loading to the final point, upgrade screens and loading
     included, pauses and the title excluded. Is that the definition you want (or match time only)?
  2. Modified runs (dev keys, debug options, Infinite difficulty changed, launch options) don't submit at all, as asked
     (no separate board). OK?
  3. FIXED LOADOUT: "the player's team" is built as the player's stat levels, perks and stat cards (not their look or a
     different squad). Should it also be able to put the player on one of the mod's teams (colours / banner)?
  4. Community tournaments unlock no Steam achievements (a 1-match tournament would otherwise give "win the game"). Agree?
  5. Difficulty is the simulator's estimate (about ±0.13 win chance per match against simulated play). Is a 1-10 number
     with "estimated clear chance" the right way to show it, or would you rather have a hard power budget per team?
  6. Cheating: the server rejects impossible results and checks run logs, but a rewritten game could still send a
     believable fake (for the boards and for a tournament's clear record). Flagging / removing is manual (SETUP.md). Enough
     for now?
  7. Reports: three players' reports hide a tournament until you look at it. Is 3 the right number?
- Settings restored after testing: DevKeys off, DumpScenes off, SkipTutorial off, ListenAddress 0.0.0.0, JoinAddress
  127.0.0.1, `[Community] Server` empty; the mod's saves restored to their state before testing, test files (tournament
  lists, test identities, queues, test replays) removed; save.json untouched.

## Session status (2026-10-22)
- **Done**: the three known bugs (net wall, perk zoom stuck, ball lost on serve) and features 1-17, each built,
  deployed, tested (keyboard, gamepad through the dev virtual pad, local multiplayer; online with two or three copies of
  the game on this PC where online was touched; the simulator for the draft) and committed.
- **Partly done**: nothing. Limits worth knowing are in each feature's notes (replays show what the host drew; the
  bracket for Classic is local only; spectators join from the next round).
- **Not tested with real people / devices**: everything online ran as copies on one PC over 127.0.0.1 (not Steam, not
  across the internet), and gamepads were the virtual dev pad. A real two-PC session and a real controller are the next
  checks.
- **Open questions**:
  1. Co-op now gets two picks per screen, with personal perks helping one character each. Is co-op still the right
     difficulty? (The simulator models one player per team.)
  2. Daily runs: the first run of the day counts and more are practice (as built). Or allow only one attempt?
  3. Draft: packages are balanced to an 80% early-match win rate in the simulator. Is that the right strength for the
     draft cup, whose opponents also draft?
  4. Party cup: two AI entrants settle their match with a weighted coin instead of playing it. Watch it instead?
  5. Protocol is 8 now: every player needs this build to play online together.
- Settings restored after testing: DevKeys off, DumpScenes off, SkipTutorial off, CheckForUpdates on, ListenAddress
  0.0.0.0, JoinAddress 127.0.0.1; the mod's saves restored to their state before the session; save.json untouched.

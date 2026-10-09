# Hangtime! Overtime — notes

The running log of this autonomous session: what was added or changed, simulator numbers before
and after every balance change, and decisions made without you. The newest entries are at the bottom
of **Log**. **Summary** at the top is kept up to date.

## Summary (kept current)

- **Everything that exists**: `CATALOG.md` (generated from the sheets by `python tools/catalog.py`) lists every team
  (class, element, base, signature perks, emblem), every perk (rarity, element, trigger, description, gameplay effects,
  visuals) and every stat card. Current totals: 26 new teams (+ the game's 10), 128 perks (118 new, 3 reworked, 7 kept),
  41 stat cards. No perk changes the score (enforced by the preflight).
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
  team's element); rares unlock at round 5, epics at round 10 (built-ins too: Kozuki's Agility waits until round 10).
- **Balance status**: fresh build wins ~70% at round 1; a typical drafted build slides from ~70% (rounds 1-5) to ~46%
  (round 10), ~40% (round 15), ~25% (round 20). Teams 48%..52% vs the field at equal rounds (36 teams in the Infinite pools: the game's 10 + 26 new).
- **Build**: `python tools/build.py [--deploy]` (preflight + generate + build); emblems: `python tools/emblems.py`.
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
   in real play will be higher than 70% — want the early rounds harder for that?
3. **Classic mode** still uses the game's own difficulty and opponents (new perks/cards do appear on its upgrade screens).
   Should new teams and the scaling system come to Classic too?
4. **All In** is a deliberately strong epic trade-off (~+16% in the sim). Keep it that way?
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

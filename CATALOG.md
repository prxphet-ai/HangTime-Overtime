# Hangtime! Overtime — catalog

Generated from `sheets/` by `python tools/catalog.py`. Numbers here are the live values.

## Teams (14 new, plus the game's 10)

| Team | Class | Element | Built on | Signature perks | Emblem |
|---|---|---|---|---|---|
| **Kagaribi Flame** | regular | fire | Rensho | Blaze Spike | brazier (`assets/emblems/kagaribi.png`) |
| **Hyoga Frost** | regular | ice | Hinami Kai | Frost Serve | penguin (`assets/emblems/hyoga.png`) |
| **Raijin Tech** | regular | lightning | Daigan Tech | Zig-Zag Spike | thunder (`assets/emblems/raijin.png`) |
| **Fujin Wings** | regular | wind | Namasito Academy | Gale Serve | swallow (`assets/emblems/fujin.png`) |
| **Kaien Marine** | regular | water | Kozuki Dan | Tidal Arc | whale (`assets/emblems/kaien.png`) |
| **Iwao Stoneworks** | combo | earth | Ten-Roku | Bedrock Block, Quake Spike | mountain (`assets/emblems/iwao.png`) |
| **Yomi Nocturne** | boss | shadow | Tenzio | Shadow Ball, Eclipse | bat moon (`assets/emblems/yomi.png`) |
| **Amaterasu Royals** | boss | light | Shirogane | Solar Flare, Holy Lance | sun crown (`assets/emblems/amaterasu.png`) |
| **Sakuradai Petals** | regular | wind | Hinami Kai | Jump Float, Swerve Tip | blossom (`assets/emblems/sakuradai.png`) |
| **Kaminari Express** | regular | lightning | Daigan Tech | Quick Set, Storm Serve | bullet train (`assets/emblems/kaminari.png`) |
| **Hinode Sparks** | regular | fire | Rensho | Ember Tip, Flame Serve | rooster (`assets/emblems/hinode.png`) |
| **Yukimura Snowcats** | combo | ice | Aomori | Permafrost, Ice Wall | snow cat (`assets/emblems/yukimura.png`) |
| **Ryujin Tide** | boss | water | Sunaumi High | Tidal Wall, Tsunami, Undertow | sea dragon (`assets/emblems/ryujin.png`) |
| **Mugen Phantoms** | boss | shadow | Tenzio | Shadow Step, Mirror Image, Eclipse | ghost (`assets/emblems/mugen.png`) |

The game's own teams keep their emblems; in Infinite they get a theme element for perk draws: Rensho (power), Hinami Kai (wind), Daigan Tech (earth), Namasito Academy (water), Kozuki Dan (lightning), Aomori (wind), Ten-Roku (earth), Tenzio (shadow), Sunaumi High (water), Shirogane (ice), Club Sumi (none)

## New perks (70)

| Perk | Rarity | Element | Trigger | What it does | Gameplay effects | Visuals |
|---|---|---|---|---|---|---|
| **Bedrock Block** | common | earth | block | Rock-solid blocks: the ball rebounds off you faster. | speed(mult 1.35) | burst, shake, sound |
| **Mud Trap** | common | earth | tip | Tips land in mud: whoever digs one gets bogged down. | on their touch: zone(width 8, mult 0.5, dur 3, color 7A5530) | burst |
| **Quake Spike** | common | earth | spike | Spikes land like a quake: digging one rattles the whole team. | on their touch: slow_enemies(mult 0.7, dur 1.6) | burst, shake |
| **Earthquake Serve** | epic | earth | serve (chance 0.55, cooldown 4) | Your serve often shakes the ground as it crosses, freezing the other team. | after the net: stun(dur 0.4, target all) | cutin, ring, shake |
| **Tectonic Slam** | epic | earth | spike (chance 0.3, cooldown 4) | Sometimes your spike splits the court and knocks the other team off balance. | stun(dur 0.5, target all) | cutin, ring, shake, sound |
| **Aftershock** | rare | earth | spike | Digging your spike leaves their hitter dazed for their counter. | on their touch: stun(dur 0.6, target spiker); on their touch: slow_enemies(mult 0.8, dur 1.5) | burst, shake |
| **Boulder Serve** | rare | earth | serve | Heavy serves: they hit harder, and the first bump against them comes up short. | on their touch: deflect(rand 8, back 0, ymult 0.45); after the net: speed(mult 1.1) | shake, trail |
| **Iron Curtain** | rare | earth | block_jump+block | Higher block jumps, and strong blocks rattle the other team. | block_jump(add 6); slow_enemies(mult 0.7, dur 1.2) | shake |
| **Blaze Spike** | common | fire | spike | Spikes burst into flame: a little extra power and a fire trail. | power(add 0.15) | burst, sound, tint, trail |
| **Ember Tip** | common | fire | tip | Tips drop embers: whoever digs one is slowed by the flames. | on their touch: zone(width 6, mult 0.5, dur 3, color FF7A2A) | trail |
| **Flame Serve** | common | fire | serve | Serves catch fire as they cross the net and speed up. | after the net: speed(mult 1.12) | burst, trail |
| **Inferno** | epic | fire | streak (n 3) | After 3 good touches in a row you ignite: much stronger spikes for 6 seconds. | buff(stat spike, mult 1.4, dur 6) | aura, cutin, flash, sound |
| **Meteor Serve** | epic | fire | serve (hold 1.0) | Hold the ball, then serve a blazing meteor that dives after the net. | speed(mult 1.3); after the net: plunge(vy 25, xmult 0.9, delay 0) | burst, cutin, trail |
| **Wildfire** | epic | fire | streak (n 5) | After 5 good touches in a row, fire sweeps the other team's court and slows them. | zone(width 14, mult 0.5, dur 4, color FF5A1A) | cutin, flash, shake |
| **Phoenix Dive** | rare | fire | dig | Your digs rise like a phoenix: extra height, and your counter-spike burns hotter. | lift(vy 13); buff(stat spike, mult 1.1, dur 2.0) | burst, sound |
| **Scorched Earth** | rare | fire | spike | Your spikes burn hotter, and where they dig one the court catches fire and slows receivers. | on their touch: zone(width 11, mult 0.4, dur 4, color FF5A1A); power(add 0.1) | burst, trail |
| **Frost Serve** | common | ice | serve | Serves freeze for a split second after the net, then rush on. | after the net: slow(mult 0.55, dur 0.3) | burst, sound, trail |
| **Frozen Wall** | common | ice | block | A strong block freezes the other team's footwork for a moment. | slow_enemies(mult 0.55, dur 2.2) | burst, tint |
| **Cold Snap** | epic | ice | spike (chance 0.45, cooldown 3) | Sometimes your spike freezes solid in mid-air, then shatters forward faster. | after the net: hover(dur 0.3, then 1.25); after the net: slowmo(scale 0.4, dur 0.25) | burst, cutin, flash |
| **Black Ice** | rare | ice | spike | Your spikes skid in on ice: a little faster, and the first bump against them slides off at an odd angle. | on their touch: deflect(rand 22, back 0, ymult 0.6); after the net: speed(mult 1.08) | burst, sound, trail |
| **Ice Wall** | rare | ice | block_jump+block | Icy block jumps reach a bit higher; strong blocks freeze the ball, then fire it back. | block_jump(add 3); hover(dur 0.25, then 1.3) | burst, tint |
| **Permafrost** | rare | ice | spike | Frost spreads where they dig your spikes: receivers on the ice move slower. | on their touch: zone(width 9, mult 0.6, dur 5, color 9FE8FF) | burst |
| **Halo Dig** | common | light | dig | When you dig, a halo guides your setter: they rush to the ball faster. | buff(stat set, mult 1.4, dur 2) | ring, tint |
| **Radiant Set** | common | light | setter_set | Your setter's sets glow gold; spike them in time for extra power. | buff(stat spike, mult 1.12, dur 1.5) | burst, tint, trail |
| **Guardian Wall** | epic | light | enemy_spike (chance 0.65, cooldown 4) | Often a golden wall slows their spike down so you can dig it. | slow(mult 0.45, dur 0.3) | cutin, flash, ring |
| **Holy Lance** | epic | light | spike (cooldown 3, min_height 8) | A spike from high up can become a lance of light: much faster. | speed(mult 1.45) | bolt, cutin, flash, sound |
| **Radiant Guard** | rare | light | enemy_spike | Their spikes make your team glow: better blocks and quicker feet for a moment. | buff(stat block, mult 1.1, dur 1.0); buff(stat move, mult 1.1, dur 1.0) | tint |
| **Read the Play** | rare | light | enemy_spike | When they spike, you read it: a burst of speed to get to the ball. | buff(stat move, mult 1.3, dur 0.8) | burst, flash |
| **Solar Flare** | rare | light | spike | Your spikes flash like the sun, blinding the other team for a split second. | slow_enemies(mult 0.8, dur 0.5) | burst, flash |
| **Lightning Reflex** | common | lightning | dig | After a dig you're charged up: a burst of speed to get under the set. | buff(stat move, mult 1.5, dur 1.8) | afterimages, burst |
| **Quick Set** | common | lightning | setter_set | Fast-tempo sets: your next spike hits a little harder. | next_power(add 0.15, window 1.8) | trail |
| **Storm Serve** | common | lightning | serve | Serves often crackle and jitter right after the net. | after the net: wobble(amp 9, period 0.12, dur 0.25, mode zigzag) | bolt, trail |
| **Thunder God** | epic | lightning | streak (n 6) | After 6 good touches in a row, lightning stuns the whole other team. | stun(dur 0.5, target all) | bolt, cutin, flash, shake, sound |
| **Thunder Serve** | rare | lightning | serve (chance 0.8) | Serves can strike their receiver with lightning as they cross the net. | after the net: stun(dur 0.45, target spiker) | bolt |
| **Thunderclap** | rare | lightning | spike (chance 0.35, cooldown 3) | Your spike can strike their blocker with lightning, stunning them for a moment. | stun(dur 0.6, target blocker) | bolt, shake, sound |
| **Zig-Zag Spike** | rare | lightning | spike | Your spikes zig-zag like lightning after the net. | after the net: wobble(amp 12, period 0.1, dur 0.35, mode zigzag) | bolt, sound, trail |
| **Libero Dive** | common | none | dig | Diving digs pop the ball up and you spring back to your feet faster. | lift(vy 4); buff(stat move, mult 1.25, dur 1.5) | ring |
| **Line Shot** | common | none | spike (chance 0.5) | Half your spikes bend toward their back line. | after the net: curve(ax 40, dur 0.3) | trail |
| **Iron Will** | common | power | block_jump | Your block jumps go higher. | block_jump(add 5) | burst |
| **Rival Spirit** | common | power | enemy_streak (n 2) | When they win two rallies in a row, your next spike hits back harder. | next_power(add 0.3, window 10) | aura, burst |
| **Aura Burst** | epic | power | streak (n 4) | After 4 good touches in a row, a golden aura boosts spike, jump and speed for 6 seconds. | buff(stat spike, mult 1.15, dur 6); buff(stat jump, mult 1.08, dur 6); buff(stat move, mult 1.15, dur 6) | aura, cutin, flash, sound |
| **Meteor Smash** | epic | power | spike (cooldown 4, min_height 9) | A spike from way up comes down like a meteor; digging it barely gets the ball up. | after the net: plunge(vy 40, xmult 0.9, delay 0); on their touch: deflect(rand 6, back 0, ymult 0.5) | burst, cutin, shake, trail |
| **Monster Block** | epic | power | block_jump+block | Higher block jumps, and a strong block sends out a shockwave that stuns the hitter. | block_jump(add 7); stun(dur 1.2, target hitter) | cutin, ring, shake, sound |
| **Spirit Serve** | epic | power | serve (hold 1.2) | Hold the ball for a moment before serving to charge a much faster spirit serve. | speed(mult 1.45) | aura, cutin, flash, trail |
| **Tempo Master** | epic | power | setter_set (cooldown 5) | Your setter controls the tempo: time slows and your spike gets a big boost. | slowmo(scale 0.5, dur 0.4); buff(stat spike, mult 1.2, dur 1.4) | cutin, flash |
| **Time Stop Set** | epic | power | setter_set (cooldown 3) | Your setter's set can freeze time: everything slows and you line up a stronger spike. | slowmo(scale 0.25, dur 0.9); buff(stat spike, mult 1.12, dur 1.2) | cutin, flash, sound |
| **Afterimage** | rare | power | passive+spike (chance 0.3) | You leave afterimages as you run, move a bit faster, and sometimes your spike fakes out the defense. | buff(stat move, mult 1.08, dur -1); after the net: decoy(offset 6, dur 0.35, fake 1) | afterimages |
| **Cut-In Spike** | rare | power | spike (cooldown 3, min_air 0.45) | Hang in the air long enough and your spike gets a dramatic close-up and extra power. | power(add 0.25); slowmo(scale 0.35, dur 0.25) | cutin, flash, zoom |
| **Echo Tip** | rare | power | tip | Your tip leaves an echo that fakes out the defense. | decoy(offset 6, dur 0.5, fake 1) | burst |
| **Gravity Drop** | rare | power | spike (chance 0.35, cooldown 3) | Sometimes your spike stops dead past the net and plunges straight down. | after the net: plunge(vy 48, xmult 0.35, delay 0.12) | ring, sound |
| **Kaiju Leap** | rare | power | jump (run 12) | Jump from a full run for a monster leap. | jump_bonus(add 15, run 12) | burst, shake |
| **Overdrive** | rare | power | dig | A dig powers you up: your next spike within 5 seconds hits much harder. | next_power(add 0.4, window 5) | aura, burst |
| **Phantom Serve** | rare | power | serve | Your serve splits into a decoy and the real ball. | after the net: decoy(offset 8, dur 0.6, fake 1) | burst |
| **Zone Focus** | rare | power | dig (chance 0.8, cooldown 3) | In the zone: sometimes a dig slows time and sharpens your counter-spike. | slowmo(scale 0.4, dur 0.5); buff(stat spike, mult 1.12, dur 1.6) | flash |
| **Night Serve** | common | shadow | serve | Serves slip into darkness just past the net. | after the net: invisible(alpha 0.3, dur 0.25) | tint |
| **Eclipse** | epic | shadow | spike (chance 0.3, cooldown 5) | Sometimes the lights go out on your spike: it vanishes and a decoy flies the other way. | after the net: invisible(alpha 0.05, dur 0.45); after the net: decoy(offset 9, dur 0.45, fake 1) | cutin, flash |
| **Shadow Ball** | rare | shadow | spike | Your spike fades into shadow for a moment after the net. | after the net: invisible(alpha 0.25, dur 0.3) | trail |
| **Shadow Step** | rare | shadow | enemy_spike | When they spike, you slip through the shadows toward the ball. | buff(stat move, mult 1.3, dur 0.8) | afterimages |
| **Mist Veil** | common | water | serve | Your serve vanishes into mist for a moment after the net. | after the net: invisible(alpha 0.45, dur 0.35) | burst |
| **Tidal Arc** | common | water | spike | Spikes ride a wave after the net: a rolling arc that's hard to read. | after the net: wobble(amp 7, period 0.3, dur 0.6, mode wave) | trail |
| **Tsunami** | epic | water | spike (chance 0.8, cooldown 3, min_height 9) | A high spike can become a tidal wave: big power, and it swamps their footwork. | power(add 0.35); after the net: slow_enemies(mult 0.7, dur 1.5); after the net: wobble(amp 6, period 0.35, dur 0.5, mode wave) | burst, cutin, shake |
| **Mirror Image** | rare | water | spike (chance 0.5) | Your spike often splits into a watery afterimage. The other team may chase the fake. | after the net: decoy(offset 7, dur 0.5, fake 1) | burst |
| **Tidal Wall** | rare | water | enemy_spike (chance 0.8) | Their spikes usually hit a wall of water and lose speed. | slow(mult 0.5, dur 0.25) | ring, sound |
| **Undertow** | rare | water | spike | Your spikes roll like a wave, and digs against them get dragged toward their back line. | on their touch: deflect(rand 0, back 20, ymult 0.85); after the net: wobble(amp 4, period 0.35, dur 0.4, mode wave) | burst, sound |
| **Gale Serve** | common | wind | serve | A gust hits your serve at the net, knocking it up or down. | after the net: gust(min -9, max 9) | ring, sound |
| **Jump Float** | common | wind | serve | Your serves float and dance unpredictably after the net. | after the net: wobble(amp 8, period 0.3, dur 0.4, mode wave) | trail |
| **Tailwind** | common | wind | passive | A constant tailwind: you run 6% faster. | buff(stat move, mult 1.06, dur -1) | afterimages |
| **Back Set** | rare | wind | setter_set (chance 0.35) | Your setter sometimes fakes the direction, leaving their blocker frozen. | stun(dur 0.4, target blocker) | ring |
| **Cyclone Spike** | rare | wind | spike | Your spike hops up over their arms, then dives. | after the net: lift(vy 9); after the net: plunge(vy 25, xmult 0.8, delay 0.2) | ring |
| **Tornado Set** | rare | wind | setter_set | Your setter's sets ride an updraft, and you jump higher to meet them. | gravity(add -2, dur 1); buff(stat jump, mult 1.08, dur 1.5) | ring |

## Reworked perks (3)

| Perk | Rarity | Element | Trigger | What it does | Gameplay effects | Visuals |
|---|---|---|---|---|---|---|
| **Afterburner** | rare | fire | spike | After your spike crosses the net it keeps speeding up, trailing fire. | after the net: accel(rate 0.7, dur 0.35) | burst, sound, trail |
| **Showboat** | common | power | tip | Every tip pumps you up: your next spike within 9 seconds gets extra power. | next_power(add 0.3, window 9) | burst, trail |
| **Second Wind** | rare | power | match_point_against | When the other team is one point from winning, you get a burst of speed and jump. | buff(stat move, mult 1.25, dur -2); buff(stat jump, mult 1.1, dur -2) | aura, cutin, flash |

## Kept from v0.1 (7)

| Perk | Rarity | Element | Trigger | What it does | Gameplay effects | Visuals |
|---|---|---|---|---|---|---|
| **Bank Shot** | common | none | set+spike | Every bump and set banks power. Your next spike cashes it all in. | bank(add 0.1, max 0.4); cash_bank() | burst |
| **Hot Streak** | common | none | spike | Each point you win in a row makes your spikes stronger. | power_streak(per 0.1, max 0.4) | burst |
| **Collector** | rare | none | spike | Your spikes get stronger for every ability you own. | power_perks(per 0.06, max 0.6) |  |
| **Overclock** | rare | none | passive+spike | Stronger spikes, but the whole game runs faster. | game_speed(mult 1.18); power(add 0.3) |  |
| **Moon Set** | common | wind | set | After your team's set or bump, the ball floats like it's on the moon. | gravity(add -2.5, dur 1.1) | burst |
| **Swerve Tip** | common | wind | tip | Your tips start short, then swerve deep into the court. | curve(ax 60, dur 0.45) | trail |
| **Dipping Serve** | rare | wind | serve | Your serves dive down sharply once they cross the net. | after the net: plunge(vy 30, xmult 0.85, delay 0.05) | trail |

## Stat cards (29)

| Card | Rarity | Style | Changes |
|---|---|---|---|
| **Marathon** | common | boost | Recovery +1.5 |
| **Platform Drills** | common | boost | Receive +0.9 |
| **Plyometrics** | common | boost | Jump +1.25 |
| **Power Drills** | common | boost | Spike +0.85 |
| **Quick Feet** | common | boost | Speed +0.6, Recovery +0.6 |
| **Rookie Grit** | common | boost | Receive +0.35, Spike +0.35 |
| **Server's Wrist** | common | boost | Serve +2 |
| **Setter Sync** | common | boost | Set +2 |
| **Sprint Training** | common | boost | Speed +0.9 |
| **Toss Practice** | common | boost | Serve +1.5 |
| **Wall Practice** | common | boost | Block +2.5 |
| **All-Rounder** | rare | boost | Spike +0.3, Jump +0.3, Block +0.3, Receive +0.3, Serve +0.3 |
| **Quick Hands** | rare | boost | Speed +0.75, Recovery +1 |
| **Ace Hunter** | rare | playstyle | Serve +1.5, Spike +1, Set -1 |
| **Anchor** | rare | playstyle | Receive +1.25, Recovery +1.5, Jump -1 |
| **Block Party** | rare | playstyle | Block +2, Jump +1, Receive -0.5 |
| **Cannon Arm** | rare | playstyle | Serve +3, Speed -0.5 |
| **Featherstep** | rare | playstyle | Speed +0.9, Jump +1, Block -1 |
| **Heavy Hitter** | rare | playstyle | Spike +2.5, Speed -0.5 |
| **Iron Wall** | rare | playstyle | Block +2.5, Receive +1.5, Spike -0.5 |
| **Libero's Instinct** | rare | playstyle | Receive +2.25, Spike -0.5 |
| **Setter Duo** | rare | playstyle | Set +3, Speed +0.75, Spike -0.25 |
| **Setter's Eye** | rare | playstyle | Set +2, Receive +1.5, Serve -1 |
| **Sky Walker** | rare | playstyle | Jump +3, Receive -1 |
| **Spike Specialist** | rare | playstyle | Spike +2, Receive -0.5 |
| **Tower** | rare | playstyle | Block +3, Jump +1.5, Speed -0.5 |
| **All In** | epic | tradeoff | Jump +1.75, Spike +1.75, Recovery -2 |
| **Glass Cannon** | epic | tradeoff | Serve +2.5, Spike +2.5, Block -1.5, Receive -1.5 |
| **Second Gear** | epic | tradeoff | Speed +1.25, Jump +1, Spike +0.75, Block -1.5, Receive -1 |

One card point is about one of the game's own level-ups (units in `sheets/stats.json`). Recovery = how fast you can move and jump again after landing; Set = your setter's speed to the ball.

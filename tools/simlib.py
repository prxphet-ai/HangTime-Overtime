"""Hangtime! Overtime match simulator: an event model of the game's rallies.

Each serve, receive, set, attack, block and dig is a probability built from the same numbers the
game uses: the stat tables, weights and AI settings exported from the player's install
(sim_data/game_data.json, written by the mod's DumpScenes option), and the mod's own sheets
(perks, stat cards, teams, scaling). Model coefficients live in sheets/sim.json; opponent scaling in
sheets/scaling.json (the same rows the mod reads). It is not the Unity physics: use it to compare.
"""
import json
import math
import os
import random

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STATS7 = ["Spike", "Jump", "Block", "Bump", "SpinServe", "ServeJump", "FloatServe"]
VISUAL = {"trail", "tint", "burst", "flash", "shake", "zoom", "cutin", "sound", "aura", "afterimages", "bolt", "ring"}


def sigmoid(x):
    return 1.0 / (1.0 + math.exp(-max(-30.0, min(30.0, x))))


# ---------------------------------------------------------------- data

class Data:
    def __init__(self, root=ROOT):
        def load(n):
            with open(os.path.join(root, "sheets", n + ".json"), encoding="utf-8") as f:
                return json.load(f)
        gd = os.path.join(root, "sim_data", "game_data.json")
        if not os.path.exists(gd):
            raise SystemExit("sim_data/game_data.json missing: run Hangtime! once with the mod's DumpScenes option on, then copy "
                             "BepInEx/plugins/HangtimeOvertime/sim_data/game_data.json into sim_data/")
        with open(gd, encoding="utf-8") as f:
            self.game = json.load(f)
        self.sim = load("sim")
        self.k = {r["id"]: r["value"] for r in self.sim["rows"]}
        self.fxm = self.sim["fx_model"]
        self.scaling = {r["id"]: r["value"] for r in load("scaling")["rows"]}
        self.scaling_doc = load("scaling")
        self.teams_doc = load("teams")
        self.perks = {p["id"]: p for p in load("perks")["rows"]}
        self.vanilla = {v["id"]: v for v in self.sim["vanilla"]}
        self.effects = {e["id"]: e for e in load("effects")["rows"]}
        self.elements = {e["id"]: e for e in load("elements")["rows"]}
        self.stat_defs = {s["id"]: s for s in load("stats")["rows"]}
        self.serve_steps = load("stats")["serve_steps"]
        sc = load("statcards")
        self.cards = {c["id"]: c for c in sc["rows"]}
        self.card_odds = sc["rarity_odds"]
        self.vanilla_cards = self.sim.get("vanilla_stat_cards", [])
        self.match_length = self.game.get("matchLength", 8)
        # team catalog: vanilla (by prefab name) + new teams (by id)
        vt = self.teams_doc["vanilla_teams"]
        self.slot_of_class = {}
        self.team_class = {}
        for cls in ("regular", "combo", "boss"):
            for n in vt[cls]:
                self.team_class[n] = cls
        for t in self.teams_doc["rows"]:
            self.team_class[t["id"]] = t["slot"]
        self.new_teams = {t["id"]: t for t in self.teams_doc["rows"]}
        self.profiles = self.teams_doc.get("vanilla_profiles", {})

    def resolve(self, key):
        """Team key from a prefab name, new-team id or display name (case-insensitive)."""
        if key in self.team_class:
            return key
        low = key.lower()
        for k in self.all_teams():
            if low in (k.lower(), self.team_name(k).lower()):
                return k
        raise SystemExit(f"unknown team '{key}'; known: {', '.join(self.all_teams())}")

    def all_teams(self):
        vt = self.teams_doc["vanilla_teams"]
        return vt["regular"] + vt["combo"] + vt["boss"] + list(self.new_teams)

    def team_name(self, key):
        if key in self.new_teams:
            return self.new_teams[key]["name"]
        g = self.game["teams"].get(key, {})
        return g.get("teamName", key)

    def element_of_team(self, key):
        if key in self.new_teams:
            return self.new_teams[key]["element"]
        return self.profiles.get(key, {}).get("element", "none")

    def perk_row(self, pid):
        if pid in self.perks:
            return self.perks[pid]
        if pid in self.vanilla:
            v = dict(self.vanilla[pid])
            v.setdefault("element", "none")
            vr = self.scaling_doc.get("vanilla_rarity", {})
            v["rarity"] = next((r for r in ("common", "rare", "epic") if pid in vr.get(r, [])), "rare")
            return v
        raise KeyError("unknown perk " + pid)


# ---------------------------------------------------------------- teams

class Side:
    """One team in a match: stat values, AI habits, abilities and perks."""

    def __init__(self, name):
        self.name = name
        self.stats = {}
        self.move = 1.5
        self.setter_move = 1.5
        self.recovery = 0.0
        self.ai = {}
        self.block_chance = 8
        self.abilities = set()
        self.perks = []            # perk rows
        self.is_player = False

    def power(self, card_points=0.0, levels=0):
        return len(self.perks) + card_points / 2.0 + levels


def _ai_of(team):
    spiker = next((p for p in team["players"] if "SpikerInput" in p), None)
    setter = next((p for p in team["players"] if "SetterInput" in p), None)
    return spiker, setter


def team_offset(D, key):
    if key in D.new_teams:
        return D.new_teams[key].get("points_offset", 0.0)
    return D.profiles.get(key, {}).get("points_offset", 0.0)


def scaled_points(D, rnd, cls, player_power=None, offset=0.0):
    s = D.scaling
    pts = s["points_base"] + s["points_per_round"] * (rnd - 1) + offset
    if cls == "boss":
        pts += s["points_boss_bonus"]
    elif cls == "combo":
        pts += s["points_combo_bonus"]
    if player_power is not None:
        expected = s["expected_power_base"] + s["expected_power_per_round"] * (rnd - 1)
        ratio = (player_power + 1.0) / (expected + 1.0)
        ratio = max(s["player_power_ratio_min"], min(s["player_power_ratio_max"], ratio))
        pts *= 1.0 + s["player_power_weight"] * (ratio - 1.0)
    return max(0.0, pts)


def _ramp(D, rnd):
    r = D.scaling["base_ramp_rounds"]
    return 1.0 if r <= 0 else max(0.0, min(1.0, (rnd - 1) / r))


def over_mult(D, rnd):
    """Stat multiplier: the round-1 base version ramps up to 1, then grows past the level cap (OpponentScaling.StatMult)."""
    s = D.scaling
    early = s["base_stat_mult"] + (1.0 - s["base_stat_mult"]) * _ramp(D, rnd)
    return early * min(s["over_max"], 1.0 + s["over_per_round"] * max(0.0, rnd - s["over_start"]))


def move_mult(D, rnd):
    s = D.scaling
    early = s["base_move_mult"] + (1.0 - s["base_move_mult"]) * _ramp(D, rnd)
    return early * min(s["move_max"], 1.0 + s["move_per_round"] * max(0.0, rnd - 1))


def perk_count(D, rnd, cls):
    s = D.scaling
    n = s["perks_base"] + s["perks_per_round"] * (rnd - 1) + (s["perks_boss_bonus"] if cls == "boss" else 0.0)
    return int(max(0, min(s["perks_max"], math.floor(n + 1e-9))))


def distribute(levels, weights, points, rng, cap=3):
    """The game's OpponentTeam.DistributeSkillPoints: weighted random level-ups, each stat capped at level 3."""
    lv = {k: 0 for k in levels}
    for _ in range(int(math.ceil(points - 1e-9))):
        pool = [(k, w) for k, w in weights.items() if w > 0 and lv.get(k, 0) < cap and k in lv]
        if not pool:
            break
        tot = sum(w for _, w in pool)
        x = rng.uniform(0, tot)
        acc = 0.0
        for k, w in pool:
            acc += w
            if x <= acc:
                lv[k] += 1
                break
    return lv


def opponent_perks(D, key, rnd, cls, rng):
    """Signature/built-in perks first, then themed random draws with rarity unlocks (scaling sheet)."""
    s = D.scaling
    count = perk_count(D, rnd, cls)
    if key in D.new_teams:
        sig = list(D.new_teams[key]["signature"])
    else:
        sig = [t for t in D.game["teams"].get(key, {}).get("techniques", []) if t in D.vanilla]
    allowed = {"common"} | ({"rare"} if rnd >= s["rare_from"] else set()) | ({"epic"} if rnd >= s["epic_from"] else set())
    chosen = [p for p in sig if D.perk_row(p)["rarity"] in allowed][:count]
    element = D.element_of_team(key)
    pool = [p for p in D.perks.values() if p["opponent_ok"] == "yes" and p["rarity"] in allowed and p["id"] not in chosen]
    pool += [D.perk_row(v) for v in D.scaling_doc["opponent_techniques"]["allowed"] if v not in chosen and D.perk_row(v)["rarity"] in allowed]
    while len(chosen) < count and pool:
        weights = [(s["theme_weight"] if p.get("element") == element and element != "none" else 1.0) for p in pool]
        p = rng.choices(pool, weights)[0]
        chosen.append(p["id"])
        pool.remove(p)
    return chosen


def build_opponent(D, key, rnd, rng, player_power=None, cls=None, no_class=False):
    key = D.resolve(key)
    base = D.new_teams[key]["base"] if key in D.new_teams else key
    g = D.game["teams"][base]
    cls = cls or D.team_class.get(key, "regular")
    if no_class:
        cls = "regular"
    weights = D.new_teams[key]["weights"] if key in D.new_teams else D.profiles.get(key, {}).get("weights", g["weights"])
    pts = scaled_points(D, rnd, cls, player_power, team_offset(D, key))
    lv = distribute(g["levels"], weights, pts, rng)
    om, mm = over_mult(D, rnd), move_mult(D, rnd)
    side = Side(D.team_name(key))
    side.key = key
    side.levels = lv
    for st in STATS7:
        side.stats[st] = g["levels"][st][min(lv.get(st, 0), len(g["levels"][st]) - 1)] * om
    spiker, setter = _ai_of(g)
    side.move = (spiker["controller"]["moveSpeed"] if spiker else 1.5) * mm
    side.setter_move = (setter["controller"]["moveSpeed"] if setter else 1.5) * mm
    side.ai = dict(spiker["SpikerInput"]) if spiker else {}
    side.block_chance = setter["SetterInput"]["blockChance"] if setter else 8
    side.perks = [D.perk_row(p) for p in opponent_perks(D, key, rnd, cls, rng)]
    for p in side.perks:
        side.abilities |= set(p.get("abilities", []))
    side.ai["shouldFloat"] = lv.get("FloatServe", 0) > lv.get("SpinServe", 0)
    return side


def build_player(D, perks=(), cards=(), vanilla_cards=(), level_ups=None):
    """The player's team: the game's player stat tables (level 0 unless level_ups), Overtime card points, perks."""
    g = D.game["player_team"]
    side = Side("Player")
    side.is_player = True
    side.key = "player"
    lv = {st: 0 for st in STATS7}
    cap = 4 if "LimitBreak" in perks else 3
    for vc in vanilla_cards:
        for st in vc["upgrades"]:
            lv[st] = min(cap, lv[st] + 1)
    if level_ups:
        for st, n in level_ups.items():
            lv[st] = min(cap, lv[st] + n)
    side.levels = lv
    for st in STATS7:
        side.stats[st] = g["levels"][st][lv[st]]
    pl = next(p for p in g["players"] if not p["controller"]["setter"])
    st_ = next(p for p in g["players"] if p["controller"]["setter"])
    side.move = pl["controller"]["moveSpeed"]
    side.setter_move = st_["controller"]["moveSpeed"]
    side.block_chance = st_.get("SetterInput", {}).get("blockChance", 5)
    side.ai = dict(D.sim["player_ai"])
    side.ai["shouldFloat"] = False
    points = {}
    for cid in cards:
        c = D.cards[cid]
        for st, v in c["deltas"].items():
            sd = D.stat_defs[st]
            points[st] = max(sd["min_steps"], min(sd["max_steps"], points.get(st, 0.0) + v))
    side.card_points = points
    for st, v in points.items():
        sd = D.stat_defs[st]
        for f in sd["fields"]:
            if f == "move":
                side.move = max(side.move * 0.6, side.move + v * sd["step"])
            elif f == "setter_move":
                side.setter_move = max(side.setter_move * 0.6, side.setter_move + v * sd["step"])
            elif f == "recovery":
                side.recovery = v
            elif f in D.serve_steps:
                side.stats[f] = max(side.stats[f] * 0.6, side.stats[f] + v * D.serve_steps[f])
            else:
                side.stats[f] = max(side.stats[f] * 0.6 if f != "Bump" else 1.0, side.stats[f] + v * sd["step"])
    side.perks = [D.perk_row(p) for p in perks]
    for p in side.perks:
        side.abilities |= set(p.get("abilities", []))
    return side


def player_power(side):
    return len(side.perks) + sum(getattr(side, "card_points", {}).values()) / 2.0 + sum(side.levels.values())


# ---------------------------------------------------------------- perks at runtime

class PerkRuntime:
    """Per-match perk state for one side: cooldowns, streaks, banks, buffs (mirrors src/Engine/Engine.cs)."""

    def __init__(self, D, side, idx, stats_out):
        self.D, self.side, self.idx, self.out = D, side, idx, stats_out
        self.cool = {}
        self.touch_streak = 0
        self.rally_wins = 0
        self.rallies_lost = 0
        self.bank = 0.0
        self.next_power = 0.0
        self.next_until = -1.0
        self.buffs = []            # (stat, mult, until)  until -1 match, -2 rally
        self.game_speed = 1.0
        self.slows = []            # (mult, until) applied to this side's movement by the enemy
        self.stunned_block_until = -1.0
        self.stunned_attack_until = -1.0
        self.stun_all_until = -1.0
        self.own_quality = 0.0     # next own set/attack quality bonus (slow-mo, float sets)
        counts = {}
        for p in side.perks:
            counts[p.get("element", "none")] = counts.get(p.get("element", "none"), 0) + 1
        self.counts = counts

    def synergy(self, perk):
        el = self.D.elements.get(perk.get("element", "none"))
        if not el:
            return 1.0
        n = self.counts.get(perk.get("element", "none"), 0)
        return el["syn3"] if n >= 3 else el["syn2"] if n >= 2 else 1.0

    def scaled(self, fx, m):
        if m == 1.0:
            return fx
        e = self.D.effects.get(fx["fx"])
        if not e or e["scales"] == "-":
            return fx
        key = e["scales"].replace("excess:", "")
        f = dict(fx)
        if e["scales"].startswith("excess:"):
            f[key] = 1.0 + (fx[key] - 1.0) * m
        else:
            f[key] = fx[key] * m
        return f

    def stat_mult(self, stat, clock):
        """Buffs that overlap the approach window before this moment count."""
        m = 1.0
        aw = self.D.k["approach_window"]
        for (s, mult, until) in self.buffs:
            if s == stat and (until in (-1.0, -2.0) or clock - aw < until):
                m *= mult
        return m

    def move_mult(self, clock):
        m = 1.0
        aw = self.D.k["approach_window"]
        for (mult, until) in self.slows:
            if clock - aw < until:
                m *= mult
        return m

    def end_rally(self):
        self.buffs = [b for b in self.buffs if b[2] != -2.0]


class Match:
    def __init__(self, D, a, b, rng, track=None):
        self.D, self.rng = D, rng
        self.sides = [a, b]
        self.track = track if track is not None else {}
        self.rt = [PerkRuntime(D, a, 0, self.track), PerkRuntime(D, b, 1, self.track)]
        self.clock = 0.0
        self.score = [0, 0]
        self.flight = None          # {"owner": i, "cross": [(fx, m)], "enemy": [(fx, m)]}
        self.touches = 0
        self.toff = 0.0             # time offset for effects that happen before the current moment (net crossings)

    # ------------------------------------------------ perk firing

    def passes(self, i, perk, trig):
        c = perk.get("conds", {})
        rt = self.rt[i]
        key = perk["id"] + ":" + trig
        if c.get("cooldown", 0) > 0 and self.clock < rt.cool.get(key, -1e9):
            return False
        if c.get("chance", 1) < 1 and self.rng.random() > c["chance"]:
            return False
        side = self.sides[i]
        cm = self.D.sim["condition_model"]
        if trig == "spike":
            jump = side.stats["Jump"] * rt.stat_mult("jump", self.clock)
            if c.get("min_air", 0) > 0 and self.rng.gauss(cm["air_time_base"] + cm["air_time_per_jump"] * jump, 0.08) < c["min_air"]:
                return False
            K = self.D.k
            if "min_height" in c and self.rng.uniform(K["spike_height_min"], K["spike_height_max"]) + K["spike_height_per_jump"] * (jump - 61.0) < c["min_height"]:
                return False
        if trig == "serve" and c.get("hold", 0) > 0 and self.rng.gauss(cm["serve_hold_mean"], cm["serve_hold_spread"]) < c["hold"]:
            return False
        if trig == "jump" and c.get("run", 0) > 0 and self.rng.random() > cm["run_jump"]:
            return False
        if c.get("match_point") and not any(s == self.D.match_length - 1 for s in self.score):
            return False
        return True

    def fire(self, i, trig, ctx):
        """Fire every perk of side i listening to trig. ctx collects immediate results (power, speed, ...)."""
        rt = self.rt[i]
        for perk in self.sides[i].perks:
            if trig not in perk["triggers"]:
                continue
            if trig == "streak" and (perk["conds"].get("n", 0) <= 0 or rt.touch_streak % int(perk["conds"]["n"]) != 0):
                continue
            if trig == "enemy_streak" and rt.rallies_lost != int(perk["conds"].get("n", 0)):
                continue
            if trig == "win_streak" and rt.rally_wins != int(perk["conds"].get("n", 0)):
                continue
            if not self.passes(i, perk, trig):
                continue
            c = perk.get("conds", {})
            if c.get("cooldown", 0) > 0:
                rt.cool[perk["id"] + ":" + trig] = self.clock + c["cooldown"]
            self.track[perk["id"]] = self.track.get(perk["id"], 0) + 1
            m = rt.synergy(perk)
            for raw in perk["fx"]:
                if raw.get("on", perk["triggers"][0]) != trig:
                    continue
                fx = rt.scaled(raw, m)
                if raw["when"] == "now":
                    self.run(i, fx, ctx)
                elif self.flight is not None and self.flight["owner"] == i:
                    self.flight["cross" if raw["when"] == "cross" else "enemy"].append(fx)

    def run(self, i, fx, ctx):
        """Apply one effect (the model's version of the engine's Run)."""
        k, D, rt, en = fx["fx"], self.D, self.rt[i], self.rt[1 - i]
        fm = D.fxm
        t = self.clock + self.toff
        if k in VISUAL:
            return
        if k == "power":
            ctx["power"] = ctx.get("power", 0.0) + fx["add"]
        elif k == "power_streak":
            ctx["power"] = ctx.get("power", 0.0) + min(rt.rally_wins * fx["per"], fx["max"])
        elif k == "power_perks":
            ctx["power"] = ctx.get("power", 0.0) + min(len(self.sides[i].perks) * fx["per"], fx["max"])
        elif k == "cash_bank":
            ctx["power"] = ctx.get("power", 0.0) + rt.bank
            rt.bank = 0.0
        elif k == "bank":
            rt.bank = min(rt.bank + fx["add"], fx["max"])
        elif k == "next_power":
            rt.next_power = max(rt.next_power, fx["add"])
            rt.next_until = t + fx["window"]
        elif k == "buff":
            until = fx["dur"] if fx["dur"] in (-1.0, -2.0, -1, -2) else t + fx["dur"]
            rt.buffs.append((fx["stat"], fx["mult"], float(until)))
        elif k == "stun":
            if fx["target"] == "all":
                en.stun_all_until = t + fx["dur"]
                en.stunned_block_until = t + fx["dur"] + D.k["t_dig"]
            elif fx["target"] == "blocker":
                en.stunned_block_until = t + fx["dur"] + D.k["t_dig"]
            else:
                en.stunned_attack_until = max(en.stunned_attack_until, t + fx["dur"])   # spiker/hitter: frozen player
        elif k == "slow_enemies":
            en.slows.append((fx["mult"], t + fx["dur"]))
        elif k == "zone":
            en.slows.append((1.0 - (1.0 - fx["mult"]) * fm["zone_coverage"], t + fx["dur"]))
        elif k == "game_speed":
            rt.game_speed = max(rt.game_speed, fx["mult"])
        elif k == "jump_bonus":
            ctx["jump"] = ctx.get("jump", 0.0) + fx["add"]
        elif k == "block_jump":
            ctx["block_jump"] = ctx.get("block_jump", 0.0) + fx["add"]
        elif k == "speed":
            ctx["speed"] = ctx.get("speed", 1.0) * fx["mult"]
        elif k == "accel":
            ctx["speed"] = ctx.get("speed", 1.0) * (1.0 + fx["rate"] * fx["dur"])
        elif k == "plunge":
            ctx["pen"] = ctx.get("pen", 0.0) + fm["plunge"] * fx["vy"] / 40.0
        elif k == "curve":
            ctx["pen"] = ctx.get("pen", 0.0) + fm["curve"] * fx["ax"] * fx["dur"] / 27.0
        elif k == "wobble":
            ctx["pen"] = ctx.get("pen", 0.0) + fm["wobble"] * fx["amp"] / 12.0 * min(1.0, fx["dur"] / 0.35)
        elif k == "hover":
            ctx["pen"] = ctx.get("pen", 0.0) + fm["hover"]
            ctx["speed"] = ctx.get("speed", 1.0) * fx["then"]
        elif k == "slow":
            ctx["pen"] = ctx.get("pen", 0.0) + fm["slow"] * (1.0 - fx["mult"]) / 0.35
        elif k == "gust":
            ctx["pen"] = ctx.get("pen", 0.0) + fm["gust"] * (fx["max"] - fx["min"]) / 18.0
        elif k == "invisible":
            ctx["pen"] = ctx.get("pen", 0.0) + fm["invisible"] * (1.0 - fx["alpha"]) * fx["dur"] / 0.3
        elif k == "decoy":
            ctx["pen"] = ctx.get("pen", 0.0) + (fm["decoy_fake"] if fx["fake"] > 0 else fm["decoy_plain"]) * fx["offset"] / 7.0
        elif k == "lift":
            ctx["quality"] = ctx.get("quality", 0.0) + fm["lift"] * fx["vy"] / 7.0
        elif k == "gravity":
            if fx["add"] < 0:
                rt.own_quality += fm["float_set"] * (-fx["add"]) / 3.0
            else:
                ctx["pen"] = ctx.get("pen", 0.0) + fm["heavy_gravity"] * fx["add"] / 6.0
        elif k == "deflect":
            ctx["deflect"] = ctx.get("deflect", 0.0) + fm["deflect"] * (fx["rand"] + fx["back"]) / 14.0 + (1.0 - fx["ymult"])
        elif k == "slowmo":
            rt.own_quality += fm["slowmo_quality"] * (1.0 - fx["scale"]) * fx["dur"] / 0.5

    # ------------------------------------------------ the rally

    def tick(self, phase):
        self.clock += self.D.k[phase]
        self.touches += 1

    def new_flight(self, owner):
        self.flight = {"owner": owner, "cross": [], "enemy": []}

    def cross(self, ctx):
        if self.flight:
            for fx in self.flight["cross"]:
                self.run(self.flight["owner"], fx, ctx)
            self.flight["cross"] = []

    def enemy_touch(self, toucher, ctx):
        if self.flight and self.flight["owner"] != toucher:
            for fx in self.flight["enemy"]:
                self.run(self.flight["owner"], fx, ctx)
            self.flight["enemy"] = []

    def accepted(self, i):
        rt = self.rt[i]
        rt.touch_streak += 1
        self.fire(i, "streak", {})

    def receive_logit(self, i, extra=0.0):
        K, side, rt = self.D.k, self.sides[i], self.rt[i]
        move = side.move * rt.stat_mult("move", self.clock) * rt.move_mult(self.clock)
        bump = side.stats["Bump"]
        x = (K["move_weight"] * (move - 1.5) / 0.3 + K["bump_weight"] * (bump - 3.0) / 2.0
             + (K["defensive_structure"] if side.ai.get("defensiveStructure") else 0.0) + 0.08 * side.recovery + extra)
        if side.is_player:
            x += K["player_skill"]
        if self.clock - self.D.k["approach_window"] < rt.stun_all_until:
            x -= self.D.fxm["stun_all"]
        elif self.clock - self.D.k["approach_window"] < rt.stunned_attack_until:
            x -= self.D.fxm["stun_one"]
        gs = max(self.rt[0].game_speed, self.rt[1].game_speed)
        x -= self.D.fxm["game_speed"] * (gs - 1.0)
        return x

    def pass_quality(self, i, speed):
        K, side = self.D.k, self.sides[i]
        perfect = K["perfect_receive_chance"] * (5.0 / 3.0 if "perfectBump" in side.abilities else 1.0)
        if speed <= 65 or self.rng.random() < perfect:
            return self.rng.uniform(0.8, 1.0)
        deflection = self.rng.uniform(speed / 3.0, speed) / max(1.0, side.stats["Bump"])
        return math.exp(-deflection / K["pass_deflect_scale"])

    def play_rally(self, server):
        D, K, rng = self.D, self.D.k, self.rng
        s, r = server, 1 - server
        S = self.sides[s]
        # --- serve
        self.new_flight(s)
        ctx = {}
        self.fire(s, "serve", ctx)
        flt = S.ai.get("shouldFloat", False) or (S.ai.get("doubleServer") and rng.random() < 0.5)
        if flt:
            fs = S.stats["FloatServe"]
            speed = 40.0 if fs < 115 else 45.0 if fs < 135 else 55.0
        else:
            speed = S.stats["SpinServe"] * (1.0 + (S.stats["ServeJump"] - 60.0) / 200.0)
        speed *= ctx.get("speed", 1.0)
        if rng.random() < K["serve_error_base"] + K["serve_error_per_speed"] * max(0.0, speed - 70.0) / 50.0:
            return self.ev("serve_error", r)
        pen = ctx.get("pen", 0.0)
        if "hybridServe" in S.abilities or "skyServe" in S.abilities:
            pen += 0.15
        self.tick("t_receive")
        cctx = {}
        self.toff = -K["t_cross_serve"]
        self.cross(cctx)
        self.toff = 0.0
        speed *= cctx.get("speed", 1.0)
        pen += cctx.get("pen", 0.0)
        if flt:
            pen += K["float_penalty"] * max(0.0, S.stats["FloatServe"] - 100.0) / 50.0
            speed = 70.0
        x = K["receive_base"] - K["receive_speed"] * (speed - 70.0) / 30.0 - pen + self.receive_logit(r)
        if rng.random() > sigmoid(x):
            return self.ev("ace", s)
        ectx = {}
        self.enemy_touch(r, ectx)
        q = self.pass_quality(r, speed) * max(0.1, 1.0 - ectx.get("deflect", 0.0))
        dctx = {}
        self.new_flight(r)
        if speed > 60:
            self.fire(r, "dig", dctx)
        self.fire(r, "set", dctx)
        self.accepted(r)
        q = min(1.0, q + dctx.get("quality", 0.0))
        return self.possession(r, q)

    def possession(self, t, q):
        """Team t has the ball after a first touch of quality q: set, attack, and the other side's defense."""
        D, K, rng = self.D, self.D.k, self.rng
        for _ in range(40):
            d = 1 - t
            T, rt = self.sides[t], self.rt[t]
            # --- set
            self.tick("t_set")
            x = K["set_base"] + K["set_quality"] * (q - 1.0) + K["setter_move_weight"] * (T.setter_move * rt.stat_mult("set", self.clock) * rt.move_mult(self.clock) - 1.5) / 0.3
            if "riskySet" in T.abilities and q < 0.5:
                x += 0.4
            if T.is_player:
                x += K["player_skill"]
            setctx = {}
            free = False
            if rng.random() < sigmoid(x):
                self.new_flight(t)
                self.fire(t, "set", setctx)
                self.fire(t, "setter_set", setctx)
                self.accepted(t)
                approach = T.move * rt.stat_mult("move", self.clock) * rt.move_mult(self.clock)
                sq = min(1.0, rng.uniform(0.7, 1.0) * (0.6 + 0.4 * q) + rt.own_quality + setctx.get("quality", 0.0)
                         + K["approach_move_weight"] * (approach - 1.5) / 0.3 * 0.25)
                rt.own_quality = 0.0
            else:
                free = "canSpikeFreeBalls" not in T.abilities
                sq = 0.5
            if self.clock < rt.stunned_attack_until:
                free = True
            # --- attack
            self.tick("t_attack")
            self.new_flight(t)
            actx = {}
            if free:
                speed, pen, kind = 45.0, 0.0, "free"
            else:
                tip_chance = T.ai.get("tipChance", 6)
                if tip_chance > 1 and rng.randrange(1, int(tip_chance)) == 1:
                    kind = "tip"
                    self.fire(t, "tip", actx)
                    speed = 35.0 * actx.get("speed", 1.0)
                else:
                    kind = "spike"
                    self.fire(t, "jump", actx)
                    self.fire(t, "spike", actx)
                    power = T.stats["Spike"] * rt.stat_mult("spike", self.clock) + actx.get("power", 0.0)
                    if rt.next_power > 0 and self.clock < rt.next_until:
                        power += rt.next_power
                        rt.next_power = 0.0
                    speed = power * D.game["ball"]["originalSPikePower"] * (0.85 + 0.15 * sq) * actx.get("speed", 1.0)
                    if rng.random() < K["spike_error_base"] + K["spike_error_per_speed"] * max(0.0, speed - 110.0) / 40.0 + K["spike_error_bad_set"] * (1.0 - sq):
                        return self.ev("spike_error", d)
                self.accepted(t)
                pen = actx.get("pen", 0.0)
            # --- defensive reactions: the other team's enemy_spike perks act on the incoming spike
            dside, drt = self.sides[d], self.rt[d]
            help_dig = 0.0
            if kind == "spike":
                dfx = {}
                self.fire(d, "enemy_spike", dfx)
                help_dig = dfx.get("pen", 0.0)        # slows/hovers on their spike help the defenders
                speed *= dfx.get("speed", 1.0)          # and a stalled spike arrives slower
            # --- block (spikes only)
            soft = False
            if kind == "spike":
                bc = dside.block_chance
                attempt = (1.0 - 1.0 / (bc - 1)) if bc > 2 else 0.0
                if self.clock < drt.stunned_block_until:
                    attempt = 0.0
                if rng.random() < attempt:
                    bctx = {}
                    self.fire(d, "block_jump", bctx)
                    jump = T.stats["Jump"] * rt.stat_mult("jump", self.clock) + actx.get("jump", 0.0)
                    block = dside.stats["Block"] * drt.stat_mult("block", self.clock) + bctx.get("block_jump", 0.0)
                    quick = 0.3 if T.ai.get("canSpikeQuicks") and rng.random() < 0.3 else 0.0
                    bx = K["block_touch_base"] + K["block_stat_weight"] * (block - 73.0) / 5.0 - K["block_jump_weight"] * (jump - 61.0) / 5.0 - quick
                    if rng.random() < sigmoid(bx):
                        self.new_flight(d)
                        kctx = {}
                        self.fire(d, "block", kctx)
                        self.accepted(d)
                        stuff = K["block_stuff_absolute"] if "absoluteBlock" in dside.abilities else K["block_stuff"]
                        if rng.random() < stuff:
                            # rebound onto the attacker's court: the attackers try to cover it
                            cover_speed = speed * 0.7 * kctx.get("speed", 1.0)
                            self.tick("t_dig")
                            x = K["dig_base"] - K["dig_speed"] * (cover_speed - 80.0) / 30.0 + self.receive_logit(t) - kctx.get("pen", 0.0)
                            if rng.random() > sigmoid(x):
                                return self.ev("stuff_block", d)
                            q = self.pass_quality(t, cover_speed)
                            self.new_flight(t)
                            continue
                        soft = True
                        speed *= 0.6
            # --- defense
            self.tick("t_dig" if kind == "spike" else "t_receive")
            cctx = {}
            self.toff = -(K["t_cross_spike"] if kind == "spike" else K["t_cross_serve"])
            self.cross(cctx)
            self.toff = 0.0
            speed *= cctx.get("speed", 1.0)
            pen += cctx.get("pen", 0.0)
            if kind == "free":
                x = K["free_ball_dig"] + self.receive_logit(d)
            elif kind == "tip":
                move = dside.move * drt.stat_mult("move", self.clock) * drt.move_mult(self.clock)
                x = (K["tip_dig_base"] + K["tip_move_weight"] * (move - 1.5) / 0.3 - pen - K["dig_speed"] * (speed - 35.0) / 30.0
                     + self.receive_logit(d, -K["move_weight"] * (move - 1.5) / 0.3))
            else:
                atk_jump = T.stats["Jump"] * rt.stat_mult("jump", self.clock) + actx.get("jump", 0.0)
                x = (K["dig_base"] - K["dig_speed"] * (speed - 80.0) / 30.0 - K["dig_set_quality"] * sq - pen + help_dig
                     - K["dig_attack_height"] * (atk_jump - 61.0) / 5.0
                     + self.receive_logit(d) + (K["soft_block_dig_bonus"] if soft else 0.0))
            if rng.random() > sigmoid(x):
                return self.ev(kind + "_kill", t)
            ectx = {}
            self.enemy_touch(d, ectx)
            q = self.pass_quality(d, speed) * max(0.1, 1.0 - ectx.get("deflect", 0.0))
            self.new_flight(d)
            dctx = {}
            if speed > 60:
                self.fire(d, "dig", dctx)
            self.fire(d, "set", dctx)
            self.accepted(d)
            q = min(1.0, q + dctx.get("quality", 0.0))
            # first-touch attackers sometimes hit straight away
            if self.sides[d].ai.get("canSpikeFirstTouch") and q > 0.6 and rng.random() < 0.15:
                q = 0.6
            t = d
        return rng.randrange(2)

    def ev(self, name, winner):
        self.track["ev:" + name] = self.track.get("ev:" + name, 0) + 1
        return winner

    def rally_end(self, winner):
        loser = 1 - winner
        for i in (0, 1):
            self.rt[i].end_rally()
        self.rt[winner].rally_wins += 1
        self.rt[winner].rallies_lost = 0
        self.rt[loser].rally_wins = 0
        self.rt[loser].rallies_lost += 1
        self.rt[loser].touch_streak = 0
        self.fire(loser, "enemy_streak", {})
        self.fire(winner, "win_streak", {})
        L = self.D.match_length
        for i in (0, 1):
            own, enemy = self.score[i], self.score[1 - i]
            if enemy == L - 1 and own < L and enemy < L:
                self.fire(i, "match_point_against", {})
        self.clock += self.D.k["seconds_between_rallies"]
        self.flight = None

    def play(self, first_server=None):
        self.fire(0, "passive", {})
        self.fire(1, "passive", {})
        server = self.rng.randrange(2) if first_server is None else first_server
        rallies, touches = 0, 0
        L = self.D.match_length
        while max(self.score) < L:
            self.touches = 0
            w = self.play_rally(server)
            self.score[w] += 1
            rallies += 1
            touches += self.touches
            self.rally_end(w)
            server = w
        return {"winner": 0 if self.score[0] >= L else 1, "score": tuple(self.score), "rallies": rallies, "touches": touches}


def play_series(D, make_a, make_b, n, rng, first_server=None):
    """n matches; make_a/make_b build fresh Sides (opponent randomness per match). Returns aggregate stats."""
    wins, pts_for, pts_against, rallies, touches = 0, 0, 0, 0, 0
    track = {}
    for _ in range(n):
        m = Match(D, make_a(), make_b(), rng, track)
        res = m.play(first_server)
        wins += res["winner"] == 0
        pts_for += res["score"][0]
        pts_against += res["score"][1]
        rallies += res["rallies"]
        touches += res["touches"]
    return {"n": n, "win_rate": wins / n, "avg_score": (pts_for / n, pts_against / n),
            "avg_rally_touches": touches / max(1, rallies), "triggers_per_match": {k: v / n for k, v in sorted(track.items())}}

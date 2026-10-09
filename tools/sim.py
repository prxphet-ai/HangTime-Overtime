"""AI vs AI match simulator for Hangtime! Overtime (see tools/simlib.py for the model).

usage:
  python tools/sim.py match   --a player --b kagaribi --round 1 --n 500
  python tools/sim.py teams   --round 5 --n 200          # every team vs every team at one round
  python tools/sim.py scaling --rounds 1,5,10,15 --n 300  # fresh player build vs each team
  python tools/sim.py perk    --perk blaze_spike --round 5 --n 400   # with vs without one perk (player build)
  python tools/sim.py perks   --round 5 --n 300           # every perk and stat card, one at a time
  python tools/sim.py run     --n 300                     # simulated player drafting through an infinite run

common options: --seed N (default 1), --label TEXT, --no-save, --perks-a a,b --cards-a x,y (player build),
                --random-perks-a K (random draw for the player build), --no-power (opponents ignore player power)
Results are printed and saved to sim_results/<mode>_<label>.json; sim_results/index.csv keeps one line per run.
"""
import argparse
import csv
import datetime
import json
import os
import random
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import simlib as S  # noqa: E402

OUT = os.path.join(S.ROOT, "sim_results")


def save(mode, label, summary, result, args):
    if args.no_save:
        return
    os.makedirs(OUT, exist_ok=True)
    name = f"{mode}_{label}" if label else mode
    with open(os.path.join(OUT, name + ".json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({"mode": mode, "label": label, "args": vars(args), "when": datetime.datetime.now().isoformat(timespec="seconds"),
                   "summary": summary, "result": result}, f, indent=1)
    idx = os.path.join(OUT, "index.csv")
    new = not os.path.exists(idx)
    with open(idx, "a", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        if new:
            w.writerow(["when", "mode", "label", "seed", "n", "summary"])
        w.writerow([datetime.datetime.now().isoformat(timespec="seconds"), mode, label, args.seed, args.n, json.dumps(summary)])
    print(f"\nsaved sim_results/{name}.json")


def player_maker(D, args, perks=None, cards=None):
    perks = list(perks if perks is not None else (args.perks_a.split(",") if args.perks_a else []))
    cards = list(cards if cards is not None else (args.cards_a.split(",") if args.cards_a else []))
    rng = random.Random(args.seed * 7919)
    if args.random_perks_a:
        pool = [p for p in D.perks if p not in perks]
        perks += rng.sample(pool, args.random_perks_a)
    return lambda: S.build_player(D, perks, cards)


def opp_maker(D, key, rnd, rng, power):
    return lambda: S.build_opponent(D, key, rnd, rng, power)


# ---------------------------------------------------------------- modes

def mode_match(D, args, rng):
    a = player_maker(D, args) if args.a == "player" else (lambda: S.build_opponent(D, args.a, args.round, rng))
    pa = None if args.no_power or args.a != "player" else S.player_power(a())
    b = player_maker(D, args) if args.b == "player" else opp_maker(D, args.b, args.round_b or args.round, rng, pa)
    res = S.play_series(D, a, b, args.n, rng)
    print(f"{args.a} vs {args.b} at round {args.round}: win {res['win_rate']:.1%}, score {res['avg_score'][0]:.1f}-{res['avg_score'][1]:.1f}, "
          f"rally {res['avg_rally_touches']:.1f} touches")
    evs = {k[3:]: v for k, v in res["triggers_per_match"].items() if k.startswith("ev:")}
    tot = sum(evs.values()) or 1
    print("how points end: " + ", ".join(f"{k} {v / tot:.0%}" for k, v in sorted(evs.items(), key=lambda kv: -kv[1])))
    top = sorted(((k, v) for k, v in res["triggers_per_match"].items() if not k.startswith("ev:")), key=lambda kv: -kv[1])[:15]
    if top:
        print("perk triggers per match: " + ", ".join(f"{k} {v:.1f}" for k, v in top))
    return {"win_rate": res["win_rate"], "avg_rally_touches": res["avg_rally_touches"]}, res


def mode_teams(D, args, rng):
    teams = args.teams.split(",") if args.teams else D.all_teams()
    field = {t: [] for t in teams}
    matrix = {}
    rallies = []
    for i, a in enumerate(teams):
        for b in teams[i + 1:]:
            res = S.play_series(D, lambda a=a: S.build_opponent(D, a, args.round, rng, no_class=args.no_class),
                                lambda b=b: S.build_opponent(D, b, args.round, rng, no_class=args.no_class), args.n, rng)
            matrix[f"{a}|{b}"] = res["win_rate"]
            field[a].append(res["win_rate"])
            field[b].append(1 - res["win_rate"])
            rallies.append(res["avg_rally_touches"])
    print(f"Team vs field at round {args.round} ({args.n} matches per pair{', no boss/combo bonus' if args.no_class else ''}):")
    rows = sorted(((t, sum(v) / len(v)) for t, v in field.items()), key=lambda kv: -kv[1])
    for t, wr in rows:
        flag = "  <-- above target" if wr > D.sim["targets"]["team_field_max"] else ""
        print(f"  {D.team_name(t):<22} {D.team_class.get(t, ''):<8} {wr:6.1%}{flag}")
    print(f"average rally: {statistics.mean(rallies):.1f} touches")
    summary = {"round": args.round, "field": {t: round(w, 3) for t, w in rows}, "max": round(rows[0][1], 3), "min": round(rows[-1][1], 3)}
    return summary, {"matrix": matrix}


def mode_scaling(D, args, rng):
    rounds = [int(r) for r in args.rounds.split(",")]
    teams = args.teams.split(",") if args.teams else D.all_teams()
    mk = player_maker(D, args)
    power = None if args.no_power else S.player_power(mk())
    table = {}
    print(f"Player build vs each team (power {power}):")
    print(f"  {'team':<22}" + "".join(f"  r{r:<5}" for r in rounds))
    for t in teams:
        row = []
        for r in rounds:
            res = S.play_series(D, mk, lambda t=t, r=r: S.build_opponent(D, t, r, rng, power), args.n, rng)
            row.append(res["win_rate"])
        table[t] = row
        print(f"  {D.team_name(t):<22}" + "".join(f"  {w:6.1%}" for w in row))
    avg = [statistics.mean(table[t][i] for t in teams) for i in range(len(rounds))]
    print(f"  {'AVERAGE':<22}" + "".join(f"  {w:6.1%}" for w in avg))
    lo, hi = D.sim["targets"]["fresh_round1"]
    if rounds[0] == 1:
        print(f"round 1 target {lo:.0%}-{hi:.0%}: {'OK' if lo <= avg[0] <= hi else 'OUT OF RANGE'}")
    return {"rounds": rounds, "average": [round(a, 3) for a in avg]}, {"table": table}


def field_win_rate(D, make_player, rnd, n, rng, power):
    teams = D.all_teams()
    tot = 0.0
    for t in teams:
        tot += S.play_series(D, make_player, lambda t=t: S.build_opponent(D, t, rnd, rng, power), n, rng)["win_rate"]
    return tot / len(teams)


def mode_perk(D, args, rng):
    base_perks = args.perks_a.split(",") if args.perks_a else []
    base_cards = args.cards_a.split(",") if args.cards_a else []
    power = None if args.no_power else S.player_power(S.build_player(D, base_perks, base_cards))
    seed = args.seed
    base = field_win_rate(D, lambda: S.build_player(D, base_perks, base_cards), args.round, args.n, random.Random(seed), power)
    if args.perk in D.cards:
        withp = field_win_rate(D, lambda: S.build_player(D, base_perks, base_cards + [args.perk]), args.round, args.n, random.Random(seed), power)
    else:
        withp = field_win_rate(D, lambda: S.build_player(D, base_perks + [args.perk], base_cards), args.round, args.n, random.Random(seed), power)
    print(f"{args.perk} at round {args.round}: field win {base:.1%} -> {withp:.1%} ({withp - base:+.1%})")
    return {"perk": args.perk, "round": args.round, "without": round(base, 3), "with": round(withp, 3), "delta": round(withp - base, 3)}, {}


def mode_perks(D, args, rng):
    base_perks = args.perks_a.split(",") if args.perks_a else []
    base_cards = args.cards_a.split(",") if args.cards_a else []
    power = None if args.no_power else S.player_power(S.build_player(D, base_perks, base_cards))
    seed = args.seed
    base = field_win_rate(D, lambda: S.build_player(D, base_perks, base_cards), args.round, args.n, random.Random(seed), power)
    items = ([("perk", p) for p in D.perks] + [("card", c) for c in D.cards] + [("vanilla", v) for v in D.vanilla if v != "LimitBreak"]
             + [("vcard", i) for i in range(len(D.vanilla_cards))])
    if args.only:
        items = [it for it in items if it[0] in args.only.split(",")]
    out = []
    for kind, pid in items:
        if kind == "card":
            mk = lambda pid=pid: S.build_player(D, base_perks, base_cards + [pid])
        elif kind == "vcard":
            mk = lambda pid=pid: S.build_player(D, base_perks, base_cards, [D.vanilla_cards[pid]])
        else:
            mk = lambda pid=pid: S.build_player(D, base_perks + [pid], base_cards)
        wr = field_win_rate(D, mk, args.round, args.n, random.Random(seed), power)
        out.append((kind, pid, wr - base))
    out.sort(key=lambda r: -r[2])
    print(f"Field win rate at round {args.round} with no perks: {base:.1%}. Change from adding one:")
    for kind, pid, d in out:
        name = D.vanilla_cards[pid]["title"] if kind == "vcard" else (D.perks.get(pid) or D.cards.get(pid) or D.vanilla.get(pid))["title"]
        print(f"  {kind:<7} {name:<20} {d:+6.1%}")
    deltas = {(D.vanilla_cards[pid]["title"] if kind == "vcard" else pid): round(d, 3) for kind, pid, d in out}
    return {"round": args.round, "base": round(base, 3), "top": str(out[0][1]), "top_delta": round(out[0][2], 3),
            "bottom": str(out[-1][1]), "bottom_delta": round(out[-1][2], 3)}, {"deltas": deltas}


def field_rates(D, rounds, n, rng, no_class=True):
    teams = D.all_teams()
    tot = {t: [] for t in teams}
    for rnd in rounds:
        for i, a in enumerate(teams):
            for b in teams[i + 1:]:
                res = S.play_series(D, lambda a=a: S.build_opponent(D, a, rnd, rng, no_class=no_class),
                                    lambda b=b: S.build_opponent(D, b, rnd, rng, no_class=no_class), n, rng)
                tot[a].append(res["win_rate"])
                tot[b].append(1 - res["win_rate"])
    return {t: sum(v) / len(v) for t, v in tot.items()}


def mode_tune(D, args, rng):
    """Nudges each team's points_offset (sheets/teams.json) toward a 50% field win rate over several rounds."""
    rounds = [int(r) for r in args.rounds.split(",")]
    path = os.path.join(S.ROOT, "sheets", "teams.json")
    history = []
    for it in range(args.iters):
        rates = field_rates(D, rounds, args.n, rng)
        spread = max(rates.values()) - min(rates.values())
        history.append({t: round(v, 3) for t, v in rates.items()})
        print(f"iteration {it + 1}: field win rates {min(rates.values()):.1%} - {max(rates.values()):.1%}")
        with open(path, encoding="utf-8") as f:
            doc = json.load(f)
        for t, wr in rates.items():
            step = max(-1.5, min(1.5, (0.5 - wr) * args.gain))
            if t in D.new_teams:
                row = next(r for r in doc["rows"] if r["id"] == t)
                row["points_offset"] = round(max(-8.0, min(8.0, row["points_offset"] + step)), 1)
            else:
                prof = doc["vanilla_profiles"][t]
                prof["points_offset"] = round(max(-8.0, min(8.0, prof.get("points_offset", 0.0) + step)), 1)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(json.dumps(doc, indent=2, ensure_ascii=False) + "\n")
        D = S.Data()
    rates = field_rates(D, rounds, args.n, rng)
    rows = sorted(rates.items(), key=lambda kv: -kv[1])
    print("final field win rates (rounds " + args.rounds + ", no boss/combo bonus):")
    for t, wr in rows:
        off = D.new_teams[t]["points_offset"] if t in D.new_teams else D.profiles.get(t, {}).get("points_offset", 0.0)
        print(f"  {D.team_name(t):<22} {wr:6.1%}   offset {off:+.1f}")
    return {"rounds": rounds, "final": {t: round(w, 3) for t, w in rows}, "max": round(rows[0][1], 3), "min": round(rows[-1][1], 3)}, {"history": history}


def offered_perks(D, owned, rng, k=2):
    pool = []
    for p in D.perks.values():
        if p["id"] not in owned:
            pool += [p["id"]] * {"common": 3, "rare": 2, "epic": 1}[p["rarity"]]
    for v in D.vanilla:
        if v not in owned and v != "LimitBreak":
            pool += [v] * 2
    picks = []
    while len(picks) < k and pool:
        c = rng.choice(pool)
        if c not in picks:
            picks.append(c)
    return picks


def offered_cards(D, rng):
    pool = [("vanilla", vc) for vc in D.vanilla_cards]
    for c in D.cards.values():
        if rng.random() < D.card_odds[c["rarity"]]:
            pool.append(("card", c["id"]))
    rng.shuffle(pool)
    return pool[:3]


def card_value(D, choice, levels):
    kind, c = choice
    if kind == "vanilla":
        return sum(1.0 for st in c["upgrades"] if levels.get(st, 0) < 3)
    return sum(D.cards[c]["deltas"].values()) * 0.5


def perk_value(D, pid, rng):
    r = D.perk_row(pid)
    return {"common": 1.0, "rare": 1.4, "epic": 1.9}.get(r.get("rarity", "rare"), 1.2) + rng.random() * 0.6


def mode_run(D, args, rng):
    """Simulated infinite runs: opponents by slot class, random team, scaled with the player's power."""
    reached = []
    picks_seen = {}
    played, won = {}, {}
    for _ in range(args.n):
        perks, cards, vcards, recent = [], [], [], []
        rnd = 1
        while rnd <= args.max_round:
            slot = (rnd - 1) % 8 + 1
            cls = "boss" if slot in (3, 6, 8) else "combo" if slot == 7 else "regular"
            pool = [t for t in D.all_teams() if D.team_class.get(t) == cls and t not in recent] or [t for t in D.all_teams() if D.team_class.get(t) == cls]
            team = rng.choice(pool)
            recent = (recent + [team])[-3:]
            player = S.build_player(D, perks, cards, vcards)
            power = S.player_power(player)
            opp = S.build_opponent(D, team, rnd, rng, power, cls)
            res = S.Match(D, player, opp, rng).play()
            played[rnd] = played.get(rnd, 0) + 1
            if res["winner"] != 0 and not args.no_stop:
                break
            won[rnd] = won.get(rnd, 0) + (res["winner"] == 0)
            wins = rnd
            if wins == 4 and "LimitBreak" not in perks:
                perks.append("LimitBreak")
            elif wins % 3 != 0:
                offer = offered_perks(D, perks, rng)
                if offer:
                    pick = max(offer, key=lambda p: perk_value(D, p, rng)) if rng.random() > D.sim["run_policy"]["explore"] else rng.choice(offer)
                    perks.append(pick)
                    picks_seen[pick] = picks_seen.get(pick, 0) + 1
            else:
                offer = offered_cards(D, rng)
                if offer:
                    pick = max(offer, key=lambda c: card_value(D, c, player.levels))
                    if pick[0] == "vanilla":
                        vcards.append(pick[1])
                    else:
                        cards.append(pick[1])
            rnd += 1
        reached.append(rnd)
    reached.sort()
    hist = {}
    for r in reached:
        hist[r] = hist.get(r, 0) + 1
    survive = {r: sum(1 for x in reached if x > r) / len(reached) for r in (1, 3, 5, 8, 10, 15, 20)}
    print(f"{args.n} simulated infinite runs (lost at round):")
    print(f"  median {statistics.median(reached)}, mean {statistics.mean(reached):.1f}, 90th percentile {reached[int(len(reached) * 0.9) - 1]}")
    print("  share of runs that beat round: " + ", ".join(f"r{r} {v:.0%}" for r, v in survive.items()))
    per_round = {r: won.get(r, 0) / played[r] for r in sorted(played) if played[r] >= 10}
    print("  win rate of the match at each round (runs that got there): " + ", ".join(f"r{r} {v:.0%}" for r, v in per_round.items()))
    return ({"median": statistics.median(reached), "mean": round(statistics.mean(reached), 2), "survive": {str(k): round(v, 3) for k, v in survive.items()},
             "per_round": {str(k): round(v, 3) for k, v in per_round.items()}}, {"lost_at": hist, "picks": picks_seen})


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("mode", choices=["match", "teams", "scaling", "perk", "perks", "run", "tune"])
    ap.add_argument("--iters", type=int, default=4, help="tune: iterations")
    ap.add_argument("--no-stop", action="store_true", help="run: keep drafting after losses (win rate per round for a typical build)")
    ap.add_argument("--only", default="", help="perks: only these kinds (perk,card,vanilla,vcard)")
    ap.add_argument("--gain", type=float, default=10.0, help="tune: offset points per 100%% win-rate gap")
    ap.add_argument("--a", default="player")
    ap.add_argument("--b", default="kagaribi")
    ap.add_argument("--round", type=int, default=1)
    ap.add_argument("--round-b", type=int, default=0)
    ap.add_argument("--rounds", default="1,5,10,15")
    ap.add_argument("--teams", default="")
    ap.add_argument("--perk", default="")
    ap.add_argument("--perks-a", default="")
    ap.add_argument("--cards-a", default="")
    ap.add_argument("--random-perks-a", type=int, default=0)
    ap.add_argument("--max-round", type=int, default=40)
    ap.add_argument("--n", type=int, default=200)
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--label", default="")
    ap.add_argument("--no-save", action="store_true")
    ap.add_argument("--no-power", action="store_true")
    ap.add_argument("--no-class", action="store_true", help="teams mode: no boss/combo bonus (intrinsic team strength)")
    args = ap.parse_args()
    D = S.Data()
    rng = random.Random(args.seed)
    summary, result = globals()["mode_" + args.mode](D, args, rng)
    save(args.mode, args.label, summary, result, args)


if __name__ == "__main__":
    main()

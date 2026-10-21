"""Draft tournament balance (sheets/draft.json), using the AI vs AI simulator (tools/simlib.py).

usage:
  python tools/draft.py power --round 3 --n 60      # every Overtime perk and stat card alone: field win-rate gain -> draft.json rows
  python tools/draft.py build --per-team 3 --n 150  # candidate packages per team, played in the simulator; the ones
                                                    # inside target_win +- win_band are kept as draft.json packages
  python tools/draft.py check --drafts 12 --n 150   # deal drafts from the packages like the mod does and replay each option

The game deals a draft from `packages` (src/Core/Drafting.cs): three different teams, one of each team's packages. A
package is built here: per team, `theme_perks` perks of the team's element when
there are enough, the rest of `perks` from any element, `cards` stat cards; redrawn until the summed power is within
`band` of `target` (the closest of `tries` draws otherwise). Powers are the simulator's field win-rate gains, so a
package's sum is an estimate; `check` plays the actual packages to see how far apart the three options really are.
"""
import argparse
import json
import os
import random
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import simlib as S  # noqa: E402

DRAFT = os.path.join(S.ROOT, "sheets", "draft.json")


def load():
    with open(DRAFT, encoding="utf-8") as f:
        return json.load(f)


def save(doc):
    with open(DRAFT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write("\n")


def field_win_rate(D, perks, cards, rnd, n, seed):
    rng = random.Random(seed)
    teams = D.all_teams()
    power = S.player_power(S.build_player(D, perks, cards))
    tot = 0.0
    for t in teams:
        tot += S.play_series(D, lambda: S.build_player(D, perks, cards), lambda t=t: S.build_opponent(D, t, rnd, rng, power), n, rng)["win_rate"]
    return tot / len(teams)


def cmd_power(D, a):
    doc = load()
    base = field_win_rate(D, [], [], a.round, a.n, a.seed)
    rows = []
    items = [("perk", p) for p in D.perks] + [("card", c) for c in D.cards]
    for i, (kind, pid) in enumerate(items):
        wr = field_win_rate(D, [pid] if kind == "perk" else [], [pid] if kind == "card" else [], a.round, a.n, a.seed)
        rows.append({"id": pid, "kind": kind, "power": round(wr - base, 3),
                     "verified": f"tools/draft.py power --round {a.round} --n {a.n} (base {base:.3f})"})
        print(f"  {i + 1:3}/{len(items)} {kind:<4} {pid:<22} {wr - base:+.3f}", flush=True)
    rows.sort(key=lambda r: (r["kind"], -r["power"]))
    doc["rows"] = rows
    save(doc)
    pk = sorted(r["power"] for r in rows if r["kind"] == "perk")
    cd = sorted(r["power"] for r in rows if r["kind"] == "card")
    st = doc["settings"]
    typical = st["perks"] * statistics.median(pk) + st["cards"] * statistics.median(cd)
    print(f"\nbase {base:.3f}; perk median {statistics.median(pk):+.3f}, card median {statistics.median(cd):+.3f}; "
          f"a typical package sums to {typical:.3f} (settings.target is {st['target']})")


# ---------------------------------------------------------------- the deal (same as src/Core/Draft.cs)

def deal_option(D, doc, element, rng):
    st = doc["settings"]
    perks = [r for r in doc["rows"] if r["kind"] == "perk" and r["power"] > st["min_power"]]
    cards = [r for r in doc["rows"] if r["kind"] == "card" and r["power"] > st["min_power"]]
    best, best_gap = None, 9.0
    for _ in range(st["tries"]):
        themed = [p for p in perks if D.perks[p["id"]]["element"] == element]
        pick = rng.sample(themed, min(st["theme_perks"], len(themed)))
        rest = [p for p in perks if p not in pick]
        pick += rng.sample(rest, st["perks"] - len(pick))
        cpick = rng.sample(cards, st["cards"])
        total = sum(r["power"] for r in pick + cpick)
        gap = abs(total - st["target"])
        if gap < best_gap:
            best, best_gap = ([p["id"] for p in pick], [c["id"] for c in cpick], total), gap
        if gap <= st["band"]:
            break
    return best


def cmd_build(D, a):
    doc = load()
    st = doc["settings"]
    rng = random.Random(a.seed)
    out, misses = [], []
    for t in D.teams_doc["rows"]:
        kept, tried = [], []
        for c in range(a.candidates):
            perks, cards, total = deal_option(D, doc, t["element"], rng)
            if any(set(perks) == set(k["perks"]) for k in tried):
                continue
            wr = field_win_rate(D, perks, cards, st["sim_round"], a.n, a.seed + c)
            entry = {"team": t["id"], "perks": perks, "cards": cards, "sim_win": round(wr, 3),
                     "verified": f"tools/draft.py build, field win at round {st['sim_round']}, n {a.n}"}
            tried.append(entry)
            if abs(wr - st["target_win"]) <= st["win_band"]:
                kept.append(entry)
            if len(kept) >= a.per_team:
                break
        if not kept:   # nothing inside the window: the closest one, reported
            kept = sorted(tried, key=lambda e: abs(e["sim_win"] - st["target_win"]))[:1]
            misses.append(f"{t['id']} ({kept[0]['sim_win']:.1%})")
        out += kept
        print(f"{t['name']:<22} kept {len(kept)} of {len(tried)}: " + ", ".join(f"{e['sim_win']:.1%}" for e in kept), flush=True)
    doc["packages"] = out
    save(doc)
    wins = [e["sim_win"] for e in out]
    print(f"\n{len(out)} packages, win {min(wins):.1%} .. {max(wins):.1%} (target {st['target_win']:.0%} +- {st['win_band']:.1%})"
          + (f"; outside the window: {', '.join(misses)}" if misses else ""))


def cmd_check(D, a):
    doc = load()
    st = doc["settings"]
    rng = random.Random(a.seed)
    by_team = {}
    for p in doc["packages"]:
        by_team.setdefault(p["team"], []).append(p)
    names = {t["id"]: t["name"] for t in D.teams_doc["rows"]}
    spreads, rates = [], []
    for d in range(a.drafts):
        opts = []
        for team in rng.sample(sorted(by_team), st["offers"]):
            pk = rng.choice(by_team[team])
            wr = field_win_rate(D, pk["perks"], pk["cards"], a.round, a.n, a.seed + 1000 + d)   # a new seed: not the build's
            opts.append((names[team], pk["sim_win"], wr, pk["perks"], pk["cards"]))
            rates.append(wr)
        spread = max(o[2] for o in opts) - min(o[2] for o in opts)
        spreads.append(spread)
        print(f"draft {d + 1}: " + " | ".join(f"{o[0]} built {o[1]:.1%} now {o[2]:.1%}" for o in opts) + f"  -> spread {spread:.1%}", flush=True)
    print(f"\n{a.drafts} drafts at round {a.round}: option win rate mean {statistics.mean(rates):.1%} "
          f"(min {min(rates):.1%}, max {max(rates):.1%}); spread within a draft: median {statistics.median(spreads):.1%}, worst {max(spreads):.1%}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("cmd", choices=["power", "build", "check"])
    ap.add_argument("--per-team", type=int, default=3)
    ap.add_argument("--candidates", type=int, default=12)
    ap.add_argument("--round", type=int, default=3)
    ap.add_argument("--n", type=int, default=60)
    ap.add_argument("--drafts", type=int, default=12)
    ap.add_argument("--seed", type=int, default=1)
    a = ap.parse_args()
    D = S.Data()
    {"power": cmd_power, "build": cmd_build, "check": cmd_check}[a.cmd](D, a)


if __name__ == "__main__":
    main()

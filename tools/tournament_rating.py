"""Community tournament difficulty rating (sheets/community.json "rating"), fitted on the AI vs AI simulator (tools/simlib.py).

usage:
  python tools/tournament_rating.py fit --configs 3000 --n 40     # random custom teams vs random player loadouts -> rating
  python tools/tournament_rating.py check --configs 400 --n 200   # the fitted formula against fresh simulated matches

The game (src/Core/Tournaments.cs, Rating) and the server (server/worker.js, rate) estimate the player's chance to win each
match with one logistic formula:
  logit = intercept + base[squad] + team_levels * (sum of the team's stat levels) + team_perks * (summed perk power)
          + team_cards * (summed card power) + player_levels * ... + player_perks * ... + player_cards * ...
Powers are sheets/draft.json's (each perk / card alone: its field win-rate gain in the simulator). A tournament's clear
chance is the product over its matches; the difficulty (1-10) comes from that. In a DRAFT tournament the player starts
with nothing and gains one pick per match won, counted as `draft_pick` power (a median offered pick, alternating perk /
card like the game's upgrade screens).
"""
import argparse
import json
import os
import random
import statistics
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import simlib as S  # noqa: E402

COMMUNITY = os.path.join(S.ROOT, "sheets", "community.json")
DRAFT = os.path.join(S.ROOT, "sheets", "draft.json")


def load(p):
    with open(p, encoding="utf-8") as f:
        return json.load(f)


def bases(D):
    vt = D.teams_doc["vanilla_teams"]
    return vt["regular"] + vt["combo"] + vt["boss"]


def powers():
    d = load(DRAFT)
    return ({r["id"]: r["power"] for r in d["rows"] if r["kind"] == "perk"}, {r["id"]: r["power"] for r in d["rows"] if r["kind"] == "card"})


def random_side(rng, D, perk_ids, card_ids, max_perks, max_cards, level_total_max):
    levels = {st: 0 for st in S.STATS7}
    for _ in range(rng.randint(0, level_total_max)):
        st = rng.choice([s for s in S.STATS7 if levels[s] < 3] or S.STATS7)
        levels[st] = min(3, levels[st] + 1)
    perks = rng.sample(perk_ids, rng.randint(0, max_perks))
    cards = [rng.choice(card_ids) for _ in range(rng.randint(0, max_cards))]
    return levels, perks, cards


def features(B, base, tl, tp, tc, pl, pp, pc, PP, CP):
    x = [1.0] + [1.0 if base == b else 0.0 for b in B[1:]]          # the first squad is the reference
    x += [sum(tl.values()), sum(PP.get(p, 0.0) for p in tp), sum(CP.get(c, 0.0) for c in tc)]
    x += [sum(pl.values()), sum(PP.get(p, 0.0) for p in pp), sum(CP.get(c, 0.0) for c in pc)]
    return x


def sample(D, configs, n, seed):
    rng = random.Random(seed)
    B = bases(D)
    PP, CP = powers()
    perk_ids = [p for p in D.perks if D.perks[p].get("opponent_ok") == "yes"]
    card_ids = list(D.cards)
    X, wins = [], []
    for i in range(configs):
        base = rng.choice(B)
        tl, tp, tc = random_side(rng, D, perk_ids, card_ids, 6, 6, 21)
        pl, pp, pc = random_side(rng, D, list(D.perks), card_ids, 8, 8, 21)
        r = S.play_series(D, lambda: S.build_player(D, pp, pc, level_ups=pl), lambda: S.build_custom(D, base, tl, tp, tc), n, rng)
        X.append(features(B, base, tl, tp, tc, pl, pp, pc, PP, CP))
        wins.append(r["win_rate"] * n)
        if (i + 1) % 250 == 0:
            print(f"  {i + 1}/{configs}", flush=True)
    return np.array(X), np.array(wins), B


def fit_logistic(X, wins, n, l2=0.01, iters=60):
    w = np.zeros(X.shape[1])
    y = wins / n
    for _ in range(iters):                                           # Newton / IRLS on the binomial likelihood
        p = 1.0 / (1.0 + np.exp(-np.clip(X @ w, -30, 30)))
        g = X.T @ ((y - p) * n) - l2 * w
        Hm = (X.T * (p * (1 - p) * n)) @ X + l2 * np.eye(X.shape[1])
        step = np.linalg.solve(Hm, g)
        w += step
        if np.max(np.abs(step)) < 1e-7:
            break
    return w


def cmd_fit(D, a):
    X, wins, B = sample(D, a.configs, a.n, a.seed)
    w = fit_logistic(X, wins, a.n)
    PP, CP = powers()
    # an offered pick: the better of the cards on a screen, roughly the 75th percentile of the powers
    pick = statistics.quantiles(sorted(list(PP.values()) + list(CP.values())), n=4)[2]
    doc = load(COMMUNITY)
    doc["rating"] = {
        "about": "Difficulty rating, fitted by tools/tournament_rating.py fit on the simulator. logit of the player's win chance per "
                 "match = intercept + base[squad] + coefficient * each sum. Do not edit by hand; refit after balance changes.",
        "intercept": round(float(w[0]), 4),
        "bases": {b: (0.0 if i == 0 else round(float(w[i]), 4)) for i, b in enumerate(B)},
        "team_levels": round(float(w[len(B)]), 4), "team_perks": round(float(w[len(B) + 1]), 4), "team_cards": round(float(w[len(B) + 2]), 4),
        "player_levels": round(float(w[len(B) + 3]), 4), "player_perks": round(float(w[len(B) + 4]), 4), "player_cards": round(float(w[len(B) + 5]), 4),
        "draft_pick": round(pick, 4),
        "verified": f"tools/tournament_rating.py fit --configs {a.configs} --n {a.n} --seed {a.seed}",
    }
    with open(COMMUNITY, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write("\n")
    pred = 1.0 / (1.0 + np.exp(-(X @ w)))
    err = np.abs(pred - wins / a.n)
    print(json.dumps(doc["rating"], indent=1))
    print(f"fit: mean |predicted - simulated| win rate {err.mean():.3f} (n={a.n} per sample, sampling noise ~{0.5 / np.sqrt(a.n):.3f})")


def cmd_check(D, a):
    r = load(COMMUNITY)["rating"]
    X, wins, B = sample(D, a.configs, a.n, a.seed + 7)
    w = np.array([r["intercept"]] + [r["bases"][b] for b in B[1:]] + [r[k] for k in ("team_levels", "team_perks", "team_cards", "player_levels", "player_perks", "player_cards")])
    pred = 1.0 / (1.0 + np.exp(-(X @ w)))
    sim = wins / a.n
    err = np.abs(pred - sim)
    print(f"{a.configs} fresh matchups x {a.n}: mean |error| {err.mean():.3f}, 90% within {np.quantile(err, 0.9):.3f}")
    for lo in np.arange(0, 1, 0.2):
        m = (pred >= lo) & (pred < lo + 0.2)
        if m.any():
            print(f"  predicted {lo:.1f}-{lo + 0.2:.1f}: {m.sum():4} matchups, simulated mean {sim[m].mean():.3f}, predicted mean {pred[m].mean():.3f}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("cmd", choices=["fit", "check"])
    ap.add_argument("--configs", type=int, default=3000)
    ap.add_argument("--n", type=int, default=40)
    ap.add_argument("--seed", type=int, default=11)
    a = ap.parse_args()
    D = S.Data()
    {"fit": cmd_fit, "check": cmd_check}[a.cmd](D, a)


if __name__ == "__main__":
    main()

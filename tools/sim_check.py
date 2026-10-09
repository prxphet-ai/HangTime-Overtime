"""Parity check: the game's opponent-scaling numbers (exported by the mod into sim_data/game_data.json)
must equal the simulator's (tools/simlib.py). usage: python tools/sim_check.py"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import simlib as S  # noqa: E402

D = S.Data()
rows = D.game.get("scaling_table")
if not rows:
    sys.exit("no scaling_table in sim_data/game_data.json: re-export with the current mod build")
bad = 0
for cls, rnd, power, pts, sm, mm, pc in rows:
    mine = (S.scaled_points(D, rnd, cls, power), S.over_mult(D, rnd), S.move_mult(D, rnd), S.perk_count(D, rnd, cls))
    theirs = (pts, sm, mm, pc)
    if any(abs(a - b) > 1e-3 for a, b in zip(mine, theirs)):
        bad += 1
        if bad <= 10:
            print(f"MISMATCH {cls} r{rnd} power {power}: sim {mine} game {theirs}")
print(f"{len(rows)} scaling rows compared, {bad} mismatch(es)")
sys.exit(1 if bad else 0)

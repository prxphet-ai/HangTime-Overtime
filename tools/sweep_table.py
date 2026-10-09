"""Combine two perk sweeps into one table with rarity bands: python tools/sweep_table.py A.json B.json [--compare OLD_A OLD_B]"""
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BANDS = {"common": (0.02, 0.07), "rare": (0.04, 0.10), "epic": (0.07, 0.14)}


def load(p):
    return json.load(open(p))["result"]["deltas"]


def main():
    a, b = load(sys.argv[1]), load(sys.argv[2])
    old = None
    if "--compare" in sys.argv:
        i = sys.argv.index("--compare")
        oa, ob = load(sys.argv[i + 1]), load(sys.argv[i + 2])
        old = {k: (oa.get(k, 0) + ob.get(k, 0)) / 2 for k in oa}
    perks = json.load(open(os.path.join(ROOT, "sheets", "perks.json"), encoding="utf-8"))["rows"]
    cards = json.load(open(os.path.join(ROOT, "sheets", "statcards.json"), encoding="utf-8"))["rows"]
    info = {p["id"]: ("perk", p["rarity"], p["title"]) for p in perks}
    info.update({c["id"]: ("card", c["rarity"], c["title"]) for c in cards})
    rows = []
    for k in a:
        kind, rar, title = info.get(k, ("vanilla", "-", k))
        avg = (a[k] + b.get(k, 0)) / 2
        lo, hi = BANDS.get(rar, (-1, 1))
        flag = "LOW" if avg < lo else "HIGH" if avg > hi else ""
        prev = f"  (was {old[k]:+.1%})" if old and k in old else ""
        rows.append((avg, f"{kind:<8}{rar:<7}{title:<22}{a[k]:+6.1%} {b.get(k, 0):+6.1%}  avg {avg:+6.1%} {flag:<4}{prev}"))
    for _, line in sorted(rows, key=lambda r: -r[0]):
        print(line)


if __name__ == "__main__":
    main()

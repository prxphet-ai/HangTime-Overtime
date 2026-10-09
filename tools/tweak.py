"""Small sheet editor for balance passes (keeps the sheets the source of truth).

usage examples:
  python tools/tweak.py perk solar_flare slow_enemies.mult=0.8
  python tools/tweak.py perk holy_lance cond.chance=0.8 cond.min_height=9 speed.mult=1.3
  python tools/tweak.py card featherstep Speed=1.25 Block=-1
Effect params address the first effect of that kind (add #2 for the second: plunge#2.vy=30).
"""
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def num(v):
    return float(v) if any(c in v for c in ".e") else int(v)


def main():
    kind, rid, *changes = sys.argv[1:]
    name = {"perk": "perks", "card": "statcards"}[kind]
    path = os.path.join(ROOT, "sheets", name + ".json")
    doc = json.load(open(path, encoding="utf-8"))
    row = next((r for r in doc["rows"] if r["id"] == rid), None)
    if row is None:
        sys.exit(f"no {kind} '{rid}'")
    for ch in changes:
        key, val = ch.split("=", 1)
        if kind == "card":
            old = row["deltas"].get(key)
            if float(val) == 0:
                row["deltas"].pop(key, None)
            else:
                row["deltas"][key] = num(val)
            print(f"{rid}.{key}: {old} -> {val}")
            continue
        target, param = key.rsplit(".", 1)
        if target == "cond":
            old = row["conds"].get(param)
            row["conds"][param] = num(val)
        else:
            fx_kind, _, nth = target.partition("#")
            matches = [f for f in row["fx"] if f["fx"] == fx_kind]
            idx = int(nth) - 1 if nth else 0
            if idx >= len(matches):
                sys.exit(f"{rid}: no effect {target}")
            old = matches[idx].get(param)
            matches[idx][param] = num(val) if param not in ("color", "at", "stat", "target", "mode", "clip", "text") else val
        print(f"{rid}.{key}: {old} -> {val}")
    open(path, "w", encoding="utf-8", newline="\n").write(json.dumps(doc, indent=2, ensure_ascii=False) + "\n")


if __name__ == "__main__":
    main()

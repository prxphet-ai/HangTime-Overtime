"""Set opponent-scaling values: python tools/set_scaling.py key=value [key=value ...] (edits sheets/scaling.json)."""
import json
import os
import sys

p = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "sheets", "scaling.json")
d = json.load(open(p, encoding="utf-8"))
rows = {r["id"]: r for r in d["rows"]}
for arg in sys.argv[1:]:
    k, v = arg.split("=")
    if k not in rows:
        sys.exit(f"unknown scaling key {k}")
    print(f"{k}: {rows[k]['value']} -> {float(v)}")
    rows[k]["value"] = float(v)
open(p, "w", encoding="utf-8", newline="\n").write(json.dumps(d, indent=2, ensure_ascii=False) + "\n")

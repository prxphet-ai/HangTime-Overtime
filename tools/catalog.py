"""Writes CATALOG.md from the sheets: every team, perk and stat card (regenerate after any content change).

usage: python tools/catalog.py
"""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VISUAL = {"trail", "tint", "burst", "flash", "shake", "zoom", "cutin", "sound", "aura", "afterimages", "bolt", "ring"}


def load(n):
    return json.load(open(os.path.join(ROOT, "sheets", n + ".json"), encoding="utf-8"))


def fx_text(f):
    p = {k: v for k, v in f.items() if k not in ("on", "when", "fx")}
    when = {"now": "", "cross": "after the net: ", "enemy_touch": "on their touch: "}[f["when"]]
    return when + f["fx"] + "(" + ", ".join(f"{k} {v}" for k, v in p.items()) + ")"


def main():
    perks, cards, teams, stats = load("perks"), load("statcards"), load("teams"), load("stats")
    L = ["# Hangtime! Overtime — catalog", "",
         "Generated from `sheets/` by `python tools/catalog.py`. Numbers here are the live values.", ""]
    L += [f"## Teams ({len(teams['rows'])} new, plus the game's 10)", "",
          "| Team | Class | Element | Built on | Signature perks | Emblem |", "|---|---|---|---|---|---|"]
    pt = {p["id"]: p["title"] for p in perks["rows"]}
    for t in teams["rows"]:
        L.append(f"| **{t['name']}** | {t['slot']} | {t['element']} | {t['base']} | {', '.join(pt.get(s, s) for s in t['signature'])} | "
                 f"{t['emblem']['mascot'].replace('_', ' ')} (`assets/emblems/{t['id']}.png`) |")
    L += ["", "The game's own teams keep their emblems; in Infinite they get a theme element for perk draws: " +
          ", ".join(f"{k} ({v['element']})" for k, v in teams["vanilla_profiles"].items() if k != "about"), ""]
    for origin, title in (("new", "New perks"), ("reworked", "Reworked perks"), ("kept", "Kept from v0.1")):
        rows = [p for p in perks["rows"] if p["origin"] == origin]
        L += [f"## {title} ({len(rows)})", "", "| Perk | Rarity | Element | Trigger | What it does | Gameplay effects | Visuals |", "|---|---|---|---|---|---|---|"]
        for p in sorted(rows, key=lambda r: (r["element"], r["rarity"], r["title"])):
            conds = ", ".join(f"{k} {v}" for k, v in p["conds"].items() if not (k == "chance" and v == 1) and not (k == "cooldown" and v == 0))
            trig = "+".join(p["triggers"]) + (f" ({conds})" if conds else "")
            game = "; ".join(fx_text(f) for f in p["fx"] if f["fx"] not in VISUAL)
            vis = ", ".join(sorted({f["fx"] for f in p["fx"] if f["fx"] in VISUAL}))
            L.append(f"| **{p['title']}** | {p['rarity']} | {p['element']} | {trig} | {p['description']} | {game} | {vis} |")
        L.append("")
    labels = {s["id"]: s["label"] for s in stats["rows"]}
    L += [f"## Stat cards ({len(cards['rows'])})", "", "| Card | Rarity | Style | Changes |", "|---|---|---|---|"]
    for c in sorted(cards["rows"], key=lambda r: (r["style"], r["rarity"], r["title"])):
        ch = ", ".join(f"{labels[k]} {'+' if v > 0 else ''}{v:g}" for k, v in c["deltas"].items())
        L.append(f"| **{c['title']}** | {c['rarity']} | {c['style']} | {ch} |")
    L += ["", "One card point is about one of the game's own level-ups (units in `sheets/stats.json`). "
          "Recovery = how fast you can move and jump again after landing; Set = your setter's speed to the ball.", ""]
    with open(os.path.join(ROOT, "CATALOG.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(L))
    print(f"wrote CATALOG.md ({len(teams['rows'])} teams, {len(perks['rows'])} perks, {len(cards['rows'])} cards)")


if __name__ == "__main__":
    main()

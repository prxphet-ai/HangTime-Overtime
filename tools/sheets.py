"""Design sheets -> preflight report and generated C#.

usage:
  python tools/sheets.py preflight   # unfilled cells, unresolved references, rule breaks, unverified rows
  python tools/sheets.py gen         # preflight, then write src/Generated/Sheets.g.cs when the sheet data is sound

The sheets in sheets/*.json are the source of truth. Game references are checked against the
player's own Assembly-CSharp.dll (needs `pip install dnfile`) and the decompiled code folder.
"""
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEETS = os.path.join(ROOT, "sheets")
SRC = os.path.join(ROOT, "src")
GAME = os.environ.get("HANGTIME_DIR", r"C:\Program Files (x86)\Steam\steamapps\common\Hangtime!")
ASM = os.path.join(GAME, "Hangtime!_Data", "Managed", "Assembly-CSharp.dll")
DECOMP = os.environ.get("HANGTIME_DECOMP", os.path.join(os.path.dirname(ROOT), "hangtime-decomp"))

NAMES = ("perks", "triggers", "effects", "elements", "stats", "statcards", "teams", "hooks", "modes", "scaling", "ui", "saves")
GAME_STATS = ["Spike", "Jump", "Block", "Bump", "SpinServe", "ServeJump", "FloatServe"]
EMPTY_OK = {"conds", "params", "text_params", "stages"}
TEXT_RULES = {
    "color": lambda v: re.fullmatch(r"[0-9A-Fa-f]{6}", v) is not None,
    "at": lambda v: v in ("ball", "owner", "enemy"),
    "stat": lambda v: v in ("spike", "jump", "block", "move", "set"),
    "target": lambda v: v in ("blocker", "spiker", "hitter", "all"),
    "mode": lambda v: v in ("zigzag", "wave"),
    "text": lambda v: len(v.strip()) > 0,
}
SPIKE_NOW_ONLY = {"power", "cash_bank", "power_streak", "power_perks"}
ONLY_ON = {"jump_bonus": "jump", "block_jump": "block_jump", "game_speed": "passive"}
MIN_NEW_PERKS, MIN_STAT_CARDS = 50, 15


def load(name):
    with open(os.path.join(SHEETS, name + ".json"), encoding="utf-8") as f:
        return json.load(f)


def game_types():
    """{type: (base, {methods}, {fields})} from the game's assembly."""
    import dnfile
    pe = dnfile.dnPE(ASM)
    out = {}
    for t in pe.net.mdtables.TypeDef:
        try:
            base = str(t.Extends.row.TypeName)
        except Exception:
            base = ""
        out[str(t.TypeName)] = (base, {str(m.row.Name) for m in t.MethodList}, {str(f.row.Name) for f in t.FieldList})
    return out


def is_technique(types, name):
    seen = set()
    while name in types and name not in seen:
        seen.add(name)
        if types[name][0] == "Technique":
            return True
        name = types[name][0]
    return False


def read(path):
    try:
        with open(path, encoding="utf-8") as f:
            return f.read()
    except OSError:
        return None


def all_src():
    out = []
    for d, _, files in os.walk(SRC):
        if os.sep + "obj" in d or os.sep + "bin" in d:
            continue
        for fn in files:
            if fn.endswith(".cs"):
                out.append(read(os.path.join(d, fn)) or "")
    return "\n".join(out)


def is_num(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def preflight():
    errors, unverified, notes = [], [], []
    S = {n: load(n) for n in NAMES}

    # 1. every row x column crossing is filled
    for name, sheet in S.items():
        cols = list(sheet["columns"].keys())
        ids = set()
        for i, row in enumerate(sheet["rows"]):
            rid = row.get("id", f"row{i}")
            if rid in ids:
                errors.append(f"{name}.{rid}: duplicate id")
            ids.add(rid)
            for c in cols:
                v = row.get(c)
                empty = v is None or (isinstance(v, str) and v.strip() == "") or (isinstance(v, (list, dict)) and not v)
                if empty and not (c in EMPTY_OK and isinstance(v, (list, dict))):
                    errors.append(f"{name}.{rid}.{c}: unfilled")
            for c in row:
                if c not in cols:
                    errors.append(f"{name}.{rid}.{c}: column not declared in sheet header")
            if str(row.get("verified", "")).strip().lower().startswith(("no", "partial")):
                unverified.append(f"{name}.{rid}")

    types = game_types()
    src = all_src()
    sound_clips = set(re.findall(r"public AudioClip (\w+);", read(os.path.join(DECOMP, "SoundLibrary.cs")) or ""))
    if not sound_clips:
        errors.append("SoundLibrary.cs not found in the decompiled folder (sound clip names can't be checked)")
    trig = {t["id"]: t for t in S["triggers"]["rows"]}
    eff = {e["id"]: e for e in S["effects"]["rows"]}
    elements = {e["id"]: e for e in S["elements"]["rows"]}
    hooks = {h["id"]: h for h in S["hooks"]["rows"]}
    perks = {p["id"]: p for p in S["perks"]["rows"]}
    vanilla_cards = set(S["perks"].get("vanilla_cards_seen", {}).get("classes", []))

    # 2. catalogs
    for t in S["triggers"]["rows"]:
        if t["source"] not in hooks:
            errors.append(f"triggers.{t['id']}.source: '{t['source']}' not in hooks sheet")
        for st in t["stages"]:
            if st not in ("now", "cross", "enemy_touch"):
                errors.append(f"triggers.{t['id']}.stages: unknown stage '{st}'")
        for c in t["conds"]:
            if c not in ("chance", "cooldown", "min_air", "min_height", "hold", "run", "n"):
                errors.append(f"triggers.{t['id']}.conds: unknown condition '{c}'")
    for e in S["effects"]["rows"]:
        if re.search(r"score|point", e["id"] + " " + e["meaning"], re.I) and "ability" not in e["meaning"]:
            errors.append(f"effects.{e['id']}: effects may not touch the score")
        cls, meth = e["impl"].split(".")
        if not re.search(r"\bstatic\s+[\w<>]+\s+" + re.escape(meth) + r"\s*\(", src) or not re.search(r"\bclass\s+" + re.escape(cls) + r"\b", src):
            errors.append(f"effects.{e['id']}.impl: {e['impl']} not implemented in src")
        for tp in e["text_params"]:
            if tp not in TEXT_RULES and tp != "clip":
                errors.append(f"effects.{e['id']}.text_params: no rule for '{tp}'")
        sc = e["scales"]
        if sc != "-" and sc.replace("excess:", "") not in e["params"]:
            errors.append(f"effects.{e['id']}.scales: '{sc}' is not one of its params")
    for el in S["elements"]["rows"]:
        if not TEXT_RULES["color"](el["color"]):
            errors.append(f"elements.{el['id']}.color: not 6-digit hex")
        for k in ("syn2", "syn3"):
            if not is_num(el[k]):
                errors.append(f"elements.{el['id']}.{k}: not a number")

    # 3. perks
    used_fx, used_trig = set(), set()
    for p in S["perks"]["rows"]:
        pid = p["id"]
        if p["rarity"] not in ("common", "rare", "epic"):
            errors.append(f"perks.{pid}.rarity: '{p['rarity']}'")
        if p["origin"] not in ("new", "reworked", "kept"):
            errors.append(f"perks.{pid}.origin: '{p['origin']}'")
        if p["element"] not in elements:
            errors.append(f"perks.{pid}.element: '{p['element']}' not in elements sheet")
        if p["opponent_ok"] not in ("yes", "no"):
            errors.append(f"perks.{pid}.opponent_ok: yes|no")
        if not is_technique(types, p["art_from"]) or p["art_from"] not in vanilla_cards:
            errors.append(f"perks.{pid}.art_from: '{p['art_from']}' is not on the game's card list")
        allowed_conds = set()
        for t in p["triggers"]:
            used_trig.add(t)
            if t not in trig:
                errors.append(f"perks.{pid}.triggers: '{t}' not in triggers sheet")
            else:
                allowed_conds |= set(trig[t]["conds"])
        for k, v in p["conds"].items():
            if k not in allowed_conds:
                errors.append(f"perks.{pid}.conds.{k}: not a condition of its triggers")
            if not is_num(v):
                errors.append(f"perks.{pid}.conds.{k}: not a number")
        if any(t in p["triggers"] for t in ("streak", "enemy_streak", "win_streak")) and not p["conds"].get("n"):
            errors.append(f"perks.{pid}.conds.n: streak triggers need n")
        triggered = set()
        for j, f in enumerate(p["fx"]):
            tag = f"perks.{pid}.fx[{j}]"
            on, when, kind = f.get("on"), f.get("when"), f.get("fx")
            if on not in p["triggers"]:
                errors.append(f"{tag}.on: '{on}' is not one of the perk's triggers")
                continue
            triggered.add(on)
            if on in trig and when not in trig[on]["stages"]:
                errors.append(f"{tag}.when: stage '{when}' not allowed after '{on}'")
            if kind not in eff:
                errors.append(f"{tag}.fx: '{kind}' not in effects sheet")
                continue
            used_fx.add(kind)
            e = eff[kind]
            want = set(e["params"]) | set(e["text_params"])
            have = set(f) - {"on", "when", "fx"}
            for m in sorted(want - have):
                errors.append(f"{tag} ({kind}): missing param '{m}'")
            for x in sorted(have - want):
                errors.append(f"{tag} ({kind}): unknown param '{x}'")
            for prm in e["params"]:
                if prm in f and not is_num(f[prm]):
                    errors.append(f"{tag} ({kind}).{prm}: not a number")
            for tp in e["text_params"]:
                if tp not in f:
                    continue
                ok = (f[tp] in sound_clips) if tp == "clip" else TEXT_RULES[tp](str(f[tp]))
                if not ok:
                    errors.append(f"{tag} ({kind}).{tp}: invalid value '{f[tp]}'")
            if kind in SPIKE_NOW_ONLY and (on != "spike" or when != "now"):
                errors.append(f"{tag} ({kind}): only on spike, stage now")
            if kind in ONLY_ON and on != ONLY_ON[kind]:
                errors.append(f"{tag} ({kind}): only on trigger {ONLY_ON[kind]}")
            if kind == "deflect" and when != "enemy_touch":
                errors.append(f"{tag} (deflect): only at stage enemy_touch")
            if kind == "buff" and f.get("dur") == -1 and on != "passive":
                errors.append(f"{tag} (buff): dur -1 (whole match) only on passive")
        for t in p["triggers"]:
            if t not in triggered:
                errors.append(f"perks.{pid}.triggers: '{t}' has no effects")
    n_new = sum(1 for p in S["perks"]["rows"] if p["origin"] == "new")
    if n_new < MIN_NEW_PERKS:
        errors.append(f"perks: {n_new} new perks, the brief asks for at least {MIN_NEW_PERKS}")
    for e in eff:
        if e not in used_fx:
            notes.append(f"effects.{e}: not used by any perk")
    for t in trig:
        if t not in used_trig:
            notes.append(f"triggers.{t}: not used by any perk")

    # 4. stats and stat cards
    stats = {s["id"]: s for s in S["stats"]["rows"]}
    for s in S["stats"]["rows"]:
        for fl in s["fields"]:
            if fl not in GAME_STATS + ["move", "setter_move", "recovery"]:
                errors.append(f"stats.{s['id']}.fields: unknown field '{fl}'")
        if s["id"] == "Serve":
            for g in s["fields"]:
                if not is_num(S["stats"]["serve_steps"].get(g)):
                    errors.append(f"stats.serve_steps.{g}: missing")
        elif not is_num(s["step"]) or s["step"] <= 0:
            errors.append(f"stats.{s['id']}.step: must be a positive number")
    if len(S["statcards"]["rows"]) < MIN_STAT_CARDS:
        errors.append(f"statcards: {len(S['statcards']['rows'])} cards, the brief asks for at least {MIN_STAT_CARDS}")
    for c in S["statcards"]["rows"]:
        if c["rarity"] not in S["statcards"]["rarity_odds"]:
            errors.append(f"statcards.{c['id']}.rarity: '{c['rarity']}'")
        if c["style"] not in ("boost", "playstyle", "tradeoff"):
            errors.append(f"statcards.{c['id']}.style: '{c['style']}'")
        for k, v in c["deltas"].items():
            if k not in stats:
                errors.append(f"statcards.{c['id']}.deltas: unknown stat '{k}'")
            if not is_num(v):
                errors.append(f"statcards.{c['id']}.deltas.{k}: not a number")
        neg = any(v < 0 for v in c["deltas"].values())
        if c["style"] in ("playstyle", "tradeoff") and not neg:
            errors.append(f"statcards.{c['id']}: a {c['style']} card needs a drawback")
        if c["style"] == "boost" and neg:
            errors.append(f"statcards.{c['id']}: a boost card has no drawbacks")

    # 5. teams
    vt = S["teams"]["vanilla_teams"]
    for t in S["teams"]["rows"]:
        tid = t["id"]
        if t["slot"] not in ("regular", "combo", "boss"):
            errors.append(f"teams.{tid}.slot: '{t['slot']}'")
        elif t["base"] not in vt.get(t["slot"], []):
            errors.append(f"teams.{tid}.base: '{t['base']}' is not a vanilla {t['slot']} team")
        if t["element"] not in elements:
            errors.append(f"teams.{tid}.element: '{t['element']}'")
        for k in ("jersey", "shorts", "banner"):
            if not TEXT_RULES["color"](t[k]):
                errors.append(f"teams.{tid}.{k}: not 6-digit hex")
        if len(t["hair"]) != 2 or not all(TEXT_RULES["color"](h) for h in t["hair"]):
            errors.append(f"teams.{tid}.hair: two hex colors (setter, spiker)")
        if sorted(t["weights"]) != sorted(GAME_STATS):
            errors.append(f"teams.{tid}.weights: needs exactly {GAME_STATS}")
        for sgn in t["signature"]:
            if sgn not in perks:
                errors.append(f"teams.{tid}.signature: '{sgn}' not in perks sheet")
            elif perks[sgn]["opponent_ok"] != "yes":
                errors.append(f"teams.{tid}.signature: '{sgn}' is not opponent_ok")
        em = t.get("emblem", {})
        mascots = re.findall(r'"(\w+)": \w+', (read(os.path.join(ROOT, "tools", "emblems.py")) or "").split("MASCOTS = {", 1)[-1].split("}", 1)[0])
        if em.get("mascot") not in mascots:
            errors.append(f"teams.{tid}.emblem: mascot '{em.get('mascot')}' not drawn by tools/emblems.py")
        if em.get("layout") not in ("top", "stacked", "split") or em.get("font") not in ("marker", "bangers"):
            errors.append(f"teams.{tid}.emblem: layout top|stacked|split, font marker|bangers")
        if not os.path.exists(os.path.join(ROOT, "assets", "emblems", tid + ".png")):
            errors.append(f"teams.{tid}.emblem: assets/emblems/{tid}.png missing (run tools/emblems.py)")
    for slot, cls in S["teams"]["slots"].items():
        if slot != "about" and cls not in ("regular", "combo", "boss"):
            errors.append(f"teams.slots.{slot}: '{cls}'")

    # 6. hooks
    for h in S["hooks"]["rows"]:
        hid, t, m = h["id"], h["target_type"], h["target_method"]
        if h["changes_score"] != "no":
            errors.append(f"hooks.{hid}.changes_score: must be 'no'")
        if h["kind"] == "event":
            short = t.rsplit(".", 1)[-1]
            code = read(os.path.join(SRC, "Patches", h["patch_class"] + ".cs"))
            if not t.startswith("UnityEngine."):
                errors.append(f"hooks.{hid}.target_type: events must be UnityEngine types")
            if code is None:
                errors.append(f"hooks.{hid}.patch_class: src/Patches/{h['patch_class']}.cs missing")
            elif f"{short}.{m} +=" not in code:
                errors.append(f"hooks.{hid}: {h['patch_class']}.cs does not subscribe to {short}.{m}")
            continue
        if t not in types:
            errors.append(f"hooks.{hid}.target_type: '{t}' not in the game")
            continue
        if h["kind"] == "field":
            for fld in m.split(","):
                if fld not in types[t][2]:
                    errors.append(f"hooks.{hid}.target_method: field '{t}.{fld}' not in the game")
            continue
        if m not in types[t][1]:
            errors.append(f"hooks.{hid}.target_method: '{t}.{m}' not in the game")
        if h["kind"] not in ("native", "prefix", "postfix"):
            errors.append(f"hooks.{hid}.kind: '{h['kind']}'")
        if h["kind"] in ("prefix", "postfix"):
            code = read(os.path.join(SRC, "Patches", h["patch_class"] + ".cs"))
            if code is None:
                errors.append(f"hooks.{hid}.patch_class: src/Patches/{h['patch_class']}.cs missing")
            else:
                pat = r"HarmonyPatch\(typeof\(" + re.escape(t) + r"\),\s*(\"" + re.escape(m) + r"\"|nameof\(" + re.escape(t) + r"\." + re.escape(m) + r"\))"
                starts = [x.start() for x in re.finditer(r"\[HarmonyPatch\(", code)] + [len(code)]
                blocks = [code[a:b] for a, b in zip(starts, starts[1:])]
                if not any(re.match(r"\[" + pat, blk) and ("[Harmony" + h["kind"].capitalize() + "]") in blk for blk in blocks):
                    errors.append(f"hooks.{hid}: no [HarmonyPatch(typeof({t}), \"{m}\")] with a Harmony{h['kind'].capitalize()} in {h['patch_class']}.cs")
        elif h["patch_class"] != "-":
            errors.append(f"hooks.{hid}.patch_class: native hooks use '-'")
    # every Harmony patch in the code has a hooks row (nothing touches the game unlisted)
    listed = {(h["target_type"], h["target_method"], h["kind"]) for h in S["hooks"]["rows"]}
    for d, _, files in os.walk(os.path.join(SRC, "Patches")):
        for fn in files:
            code = read(os.path.join(d, fn)) or ""
            starts = [x.start() for x in re.finditer(r"\[HarmonyPatch\(", code)] + [len(code)]
            for a, b in zip(starts, starts[1:]):
                blk = code[a:b]
                m = re.match(r"\[HarmonyPatch\(typeof\((\w+)\),\s*\"(\w+)\"", blk)
                if not m:
                    continue
                for kind in ("prefix", "postfix"):
                    if f"[Harmony{kind.capitalize()}]" in blk and (m.group(1), m.group(2), kind) not in listed:
                        errors.append(f"hooks: {fn} patches {m.group(1)}.{m.group(2)} ({kind}) with no hooks row")
    for score_m in ("PlayerGotPoint", "OpponentGotPoint"):
        for h in S["hooks"]["rows"]:
            if h["target_method"] == score_m and h["kind"] == "prefix":
                errors.append(f"hooks.{h['id']}: no prefixes on {score_m} (could change the score)")

    # 7. modes, scaling, ui, saves
    if {m["id"] for m in S["modes"]["rows"]} != {"Normal", "Infinite", "Loop"}:
        errors.append("modes: ids should be Normal, Infinite, Loop")
    save_ids = {s["id"] for s in S["saves"]["rows"]}
    for m in S["modes"]["rows"]:
        if m["achievements"] not in ("allow", "block"):
            errors.append(f"modes.{m['id']}.achievements: allow|block")
        if m["best_key"] != "-" and m["best_key"] not in save_ids:
            errors.append(f"modes.{m['id']}.best_key: '{m['best_key']}' not in saves sheet")
    need = {"points_base", "points_per_round", "points_boss_bonus", "points_combo_bonus", "base_stat_mult", "base_move_mult", "base_ramp_rounds",
            "over_start", "over_per_round", "over_max", "move_per_round", "move_max", "perks_base", "perks_per_round", "perks_boss_bonus",
            "perks_max", "rare_from", "epic_from", "theme_weight", "player_power_weight", "player_power_ratio_min", "player_power_ratio_max",
            "expected_power_base", "expected_power_per_round"}
    have = {s["id"] for s in S["scaling"]["rows"]}
    for m in sorted(need - have):
        errors.append(f"scaling: missing row '{m}' (read by src/Engine/OpponentScaling.cs and tools/simlib.py)")
    for s in S["scaling"]["rows"]:
        if not is_num(s["value"]):
            errors.append(f"scaling.{s['id']}.value: not a number")
    for k, v in S["teams"].get("vanilla_profiles", {}).items():
        if k != "about" and v.get("element") not in elements:
            errors.append(f"teams.vanilla_profiles.{k}: element '{v.get('element')}' not in elements sheet")
    for name in S["scaling"]["opponent_techniques"]["allowed"]:
        code = read(os.path.join(DECOMP, name + ".cs"))
        if not is_technique(types, name) or code is None:
            errors.append(f"scaling.opponent_techniques: '{name}' not a checkable Technique")
        else:
            for bad in ("timer", "statOverload", "StatTracker", "LimitBreakEffects"):
                if bad in code:
                    errors.append(f"scaling.opponent_techniques: {name} uses '{bad}' (unsafe for opponents)")
    runtime = {"wins", "best", "loop"} | save_ids
    for u in S["ui"]["rows"]:
        for field in ("text", "subtext"):
            for ph in re.findall(r"\{(\w+)\}", u[field]):
                if ph not in runtime:
                    errors.append(f"ui.{u['id']}.{field}: placeholder {{{ph}}} does not resolve")
    for s in S["saves"]["rows"]:
        if s["type"] != "int":
            errors.append(f"saves.{s['id']}.type: only int supported")
    return S, errors, unverified, notes


# ---------------------------------------------------------------- generator

def const(s):
    return "".join(w.capitalize() for w in re.split(r"[_\W]+", s) if w)


def cs(s):
    return '"' + str(s).replace("\\", "\\\\").replace('"', '\\"') + '"'


def fl(x):
    return repr(float(x)) + "f"


def arr(items, fn=cs):
    return "new[] { " + ", ".join(fn(i) for i in items) + " }" if items else "System.Array.Empty<string>()"


def generate(S):
    eff = {e["id"]: e for e in S["effects"]["rows"]}
    L = ["// <auto-generated> by tools/sheets.py from sheets/*.json. Edit the sheets, not this file. </auto-generated>",
         "namespace HangtimeOvertime.Generated", "{"]
    L += ["    public readonly struct CondDef",
          "    {",
          "        public readonly float Chance, Cooldown, N, MinAir, MinHeight, Hold, Run;",
          "        public CondDef(float chance, float cooldown, float n, float minAir, float minHeight, float hold, float run)",
          "        { Chance = chance; Cooldown = cooldown; N = n; MinAir = minAir; MinHeight = minHeight; Hold = hold; Run = run; }",
          "    }", "",
          "    // One effect use: number params in the effects sheet's order go to A..D, text params to S1, S2.",
          "    public readonly struct FxDef",
          "    {",
          "        public readonly string On, When, Kind, S1, S2;",
          "        public readonly float A, B, C, D;",
          "        public FxDef(string on, string when, string kind, float a, float b, float c, float d, string s1, string s2)",
          "        { On = on; When = when; Kind = kind; A = a; B = b; C = c; D = d; S1 = s1; S2 = s2; }",
          "    }", "",
          "    public sealed class PerkDef",
          "    {",
          "        public string Id, Title, Rarity, Element, Origin, Description, Flavour, ArtFrom;",
          "        public bool OpponentOk;",
          "        public string[] Triggers;",
          "        public CondDef Cond;",
          "        public FxDef[] Fx;",
          "    }", "",
          "    public static class Perks", "    {", "        public static readonly PerkDef[] All =", "        {"]
    for p in S["perks"]["rows"]:
        c = p["conds"]
        cond = f"new CondDef({fl(c.get('chance', 1))}, {fl(c.get('cooldown', 0))}, {fl(c.get('n', 0))}, {fl(c.get('min_air', 0))}, {fl(c.get('min_height', -99))}, {fl(c.get('hold', 0))}, {fl(c.get('run', 0))})"
        fxs = []
        for f in p["fx"]:
            e = eff[f["fx"]]
            nums = [f[k] for k in e["params"]] + [0] * (4 - len(e["params"]))
            texts = [str(f[k]) for k in e["text_params"]] + [""] * (2 - len(e["text_params"]))
            fxs.append(f"new FxDef({cs(f['on'])}, {cs(f['when'])}, {cs(f['fx'])}, {', '.join(fl(n) for n in nums)}, {cs(texts[0])}, {cs(texts[1])})")
        L.append("            new PerkDef { " + ", ".join([
            f"Id = {cs(p['id'])}", f"Title = {cs(p['title'])}", f"Rarity = {cs(p['rarity'])}", f"Element = {cs(p['element'])}",
            f"Origin = {cs(p['origin'])}", f"Description = {cs(p['description'])}", f"Flavour = {cs(p['flavour'])}",
            f"ArtFrom = {cs(p['art_from'])}", f"OpponentOk = {'true' if p['opponent_ok'] == 'yes' else 'false'}",
            f"Triggers = {arr(p['triggers'])}", f"Cond = {cond}",
            "Fx = new[] {\n                " + ",\n                ".join(fxs) + " }"]) + " },")
    L += ["        };", "    }", ""]

    L += ["    public readonly struct FxKindDef",
          "    {",
          "        public readonly string Id, Kind;",
          "        public readonly int ScaleSlot;      // number slot a synergy strengthens (-1 none)",
          "        public readonly bool ScaleExcess;   // scale the distance from 1 instead of the value",
          "        public FxKindDef(string id, string kind, int scaleSlot, bool scaleExcess) { Id = id; Kind = kind; ScaleSlot = scaleSlot; ScaleExcess = scaleExcess; }",
          "    }", "",
          "    public static class FxKinds", "    {", "        public static readonly FxKindDef[] All =", "        {"]
    for e in S["effects"]["rows"]:
        sc = e["scales"]
        slot = -1 if sc == "-" else e["params"].index(sc.replace("excess:", ""))
        L.append(f"            new FxKindDef({cs(e['id'])}, {cs(e['kind'])}, {slot}, {'true' if sc.startswith('excess:') else 'false'}),")
    L += ["        };", "    }", ""]

    L += ["    public readonly struct ElementDef",
          "    {",
          "        public readonly string Id, Color, Callout;",
          "        public readonly float Syn2, Syn3;",
          "        public ElementDef(string id, string color, float syn2, float syn3, string callout) { Id = id; Color = color; Syn2 = syn2; Syn3 = syn3; Callout = callout; }",
          "    }", "",
          "    public static class Elements", "    {", "        public static readonly ElementDef[] All =", "        {"]
    for e in S["elements"]["rows"]:
        L.append(f"            new ElementDef({cs(e['id'])}, {cs(e['color'])}, {fl(e['syn2'])}, {fl(e['syn3'])}, {cs(e['callout'])}),")
    L += ["        };", "    }", ""]

    ss = S["stats"]["serve_steps"]
    L += ["    public readonly struct StatDef",
          "    {",
          "        public readonly string Id, Label;",
          "        public readonly string[] Fields;",
          "        public readonly float Step, MinSteps, MaxSteps;",
          "        public StatDef(string id, string[] fields, float step, float min, float max, string label) { Id = id; Fields = fields; Step = step; MinSteps = min; MaxSteps = max; Label = label; }",
          "    }", "",
          "    public static class Stats", "    {",
          f"        public const float ServeSpin = {fl(ss['SpinServe'])}, ServeJump = {fl(ss['ServeJump'])}, ServeFloat = {fl(ss['FloatServe'])};",
          "        public static readonly StatDef[] All =", "        {"]
    for s in S["stats"]["rows"]:
        L.append(f"            new StatDef({cs(s['id'])}, {arr(s['fields'])}, {fl(s['step'])}, {fl(s['min_steps'])}, {fl(s['max_steps'])}, {cs(s['label'])}),")
    L += ["        };", "    }", ""]

    ro = S["statcards"]["rarity_odds"]
    L += ["    public sealed class StatCardDef",
          "    {",
          "        public string Id, Title, Rarity, Style, Blurb;",
          "        public string[] Stats;",
          "        public float[] Points;",
          "    }", "",
          "    public static class StatCards", "    {",
          f"        public const float OddsCommon = {fl(ro['common'])}, OddsRare = {fl(ro['rare'])}, OddsEpic = {fl(ro['epic'])};",
          "        public static readonly StatCardDef[] All =", "        {"]
    for c in S["statcards"]["rows"]:
        L.append("            new StatCardDef { " + ", ".join([
            f"Id = {cs(c['id'])}", f"Title = {cs(c['title'])}", f"Rarity = {cs(c['rarity'])}", f"Style = {cs(c['style'])}", f"Blurb = {cs(c['blurb'])}",
            f"Stats = {arr(list(c['deltas'].keys()))}", "Points = new[] { " + ", ".join(fl(v) for v in c["deltas"].values()) + " }"]) + " },")
    L += ["        };", "    }", ""]

    T = S["teams"]
    L += ["    public sealed class TeamDef",
          "    {",
          "        public string Id, Name, Base, Slot, Element, Jersey, Shorts, Banner, Intro, Win, Lose, Comment;",
          "        public float Pitch, PointsOffset;",
          "        public string[] Hair, GotPoint, LostPoint, Signature, WeightStats;",
          "        public float[] Weights;",
          "    }", "",
          "    public static class Teams", "    {",
          f"        public static readonly string[] VanillaRegular = {arr(T['vanilla_teams']['regular'])};",
          f"        public static readonly string[] VanillaCombo = {arr(T['vanilla_teams']['combo'])};",
          f"        public static readonly string[] VanillaBoss = {arr(T['vanilla_teams']['boss'])};",
          "        public static readonly string[] SlotClass = { \"\", " + ", ".join(cs(T["slots"][str(i)]) for i in range(1, 9)) + " };",
          "        public static string VanillaElement(string team)", "        {", "            switch (team)", "            {"]
    for k, v in T.get("vanilla_profiles", {}).items():
        if k != "about":
            L.append(f"                case {cs(k)}: return {cs(v['element'])};")
    L += ["                default: return \"none\";", "            }", "        }",
          "        public static float VanillaPointsOffset(string team)", "        {", "            switch (team)", "            {"]
    for k, v in T.get("vanilla_profiles", {}).items():
        if k != "about":
            L.append(f"                case {cs(k)}: return {fl(v.get('points_offset', 0))};")
    L += ["                default: return 0f;", "            }", "        }",
          "        public static System.Collections.Generic.List<StatWeight> VanillaWeights(string team)", "        {", "            switch (team)", "            {"]
    for k, v in T.get("vanilla_profiles", {}).items():
        if k != "about" and "weights" in v:
            items = ", ".join(f"new StatWeight {{ statName = {cs(sn)}, weight = {fl(w)} }}" for sn, w in v["weights"].items())
            L.append(f"                case {cs(k)}: return new System.Collections.Generic.List<StatWeight>({len(v['weights'])}) {{ {items} }};")
    L += ["                default: return null;", "            }", "        }",
          "        public static readonly TeamDef[] All =", "        {"]
    for t in T["rows"]:
        L.append("            new TeamDef { " + ", ".join([
            f"Id = {cs(t['id'])}", f"Name = {cs(t['name'])}", f"Base = {cs(t['base'])}", f"Slot = {cs(t['slot'])}", f"Element = {cs(t['element'])}",
            f"Jersey = {cs(t['jersey'])}", f"Shorts = {cs(t['shorts'])}", f"Banner = {cs(t['banner'])}", f"Intro = {cs(t['intro'])}",
            f"Win = {cs(t['win'])}", f"Lose = {cs(t['lose'])}", f"Comment = {cs(t['comment'])}", f"Pitch = {fl(t['pitch'])}", f"PointsOffset = {fl(t['points_offset'])}",
            f"Hair = {arr(t['hair'])}", f"GotPoint = {arr(t['got_point'])}", f"LostPoint = {arr(t['lost_point'])}", f"Signature = {arr(t['signature'])}",
            f"WeightStats = {arr(list(t['weights'].keys()))}", "Weights = new[] { " + ", ".join(fl(v) for v in t["weights"].values()) + " }"]) + " },")
    L += ["        };", "    }", ""]

    L += ["    public readonly struct HookDef",
          "    {",
          "        public readonly string Id, TargetType, TargetMethod, Kind, PatchClass;",
          "        public HookDef(string id, string t, string m, string kind, string patchClass) { Id = id; TargetType = t; TargetMethod = m; Kind = kind; PatchClass = patchClass; }",
          "    }", "",
          "    public static class Hooks", "    {", "        public static readonly HookDef[] All =", "        {"]
    for h in S["hooks"]["rows"]:
        L.append(f"            new HookDef({cs(h['id'])}, {cs(h['target_type'])}, {cs(h['target_method'])}, {cs(h['kind'])}, {cs(h['patch_class'])}),")
    L += ["        };", "    }", ""]

    L += ["    public readonly struct ModeDef",
          "    {",
          "        public readonly string Id, Banner, BestKey;",
          "        public readonly bool BlockAchievements;",
          "        public ModeDef(string id, string banner, string bestKey, bool blockAchievements) { Id = id; Banner = banner; BestKey = bestKey; BlockAchievements = blockAchievements; }",
          "    }", "",
          "    public static class Modes", "    {"]
    for m in S["modes"]["rows"]:
        L.append(f"        public static readonly ModeDef {m['id']} = new ModeDef({cs(m['id'])}, {cs(m['banner'])}, {cs(m['best_key'])}, {'true' if m['achievements'] == 'block' else 'false'});")
    L += ["    }", ""]

    L += ["    // OPPONENT SCALING CONFIG (sheets/scaling.json): one constant per row.",
          "    public static class Scaling", "    {"]
    for s in S["scaling"]["rows"]:
        L.append(f"        public const float {const(s['id'])} = {fl(s['value'])};   // {s['meaning']}")
    L += [f"        public static readonly string[] OpponentTechniques = {arr(S['scaling']['opponent_techniques']['allowed'])};",
          "        public static string VanillaRarity(string technique)", "        {"]
    for rar in ("common", "rare", "epic"):
        L.append(f"            if (System.Array.IndexOf({arr(S['scaling']['vanilla_rarity'][rar])}, technique) >= 0) return {cs(rar)};")
    L += ["            return \"rare\";", "        }", "    }", ""]

    L += ["    public readonly struct UiDef",
          "    {",
          "        public readonly string Id, Scene, Text, Subtext;",
          "        public readonly float OffsetY;",
          "        public UiDef(string id, string scene, string text, string subtext, float offsetY) { Id = id; Scene = scene; Text = text; Subtext = subtext; OffsetY = offsetY; }",
          "    }", "",
          "    public static class Ui", "    {"]
    for u in S["ui"]["rows"]:
        L.append(f"        public static readonly UiDef {const(u['id'])} = new UiDef({cs(u['id'])}, {cs(u['scene'])}, {cs(u['text'])}, {cs(u['subtext'])}, {fl(u['offset_y'])});")
    L += ["    }", ""]

    L += ["    [System.Serializable]", "    public class OvertimeSave", "    {"]
    for s in S["saves"]["rows"]:
        L.append(f"        public int {s['id']} = {int(s['default'])};")
    L += ["    }", "}", ""]
    out = os.path.join(SRC, "Generated", "Sheets.g.cs")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(L))
    return out


CODE_ERRORS = ("not implemented in src", "missing", "does not subscribe", "no [HarmonyPatch")


def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else "preflight"
    S, errors, unverified, notes = preflight()
    print(f"preflight: {len(errors)} problem(s), {len(unverified)} unverified row(s), {len(notes)} note(s)")
    for e in errors:
        print("  FIX  " + e)
    for n in notes:
        print("  NOTE " + n)
    if "-v" in sys.argv:
        for u in unverified:
            print("  TODO verify " + u)
    if cmd == "gen":
        data_errors = [e for e in errors if not any(k in e for k in CODE_ERRORS)]
        if data_errors:
            print("not generating: fix the sheet problems above first")
            sys.exit(1)
        print("wrote " + generate(S))
    sys.exit(1 if errors else 0)


if __name__ == "__main__":
    main()

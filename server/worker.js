// Hangtime! Overtime community server: global leaderboards and community tournaments.
// A Cloudflare Worker with a D1 (SQLite) database; free tier. No secret keys anywhere: a player proves who they are
// with a random secret their game keeps locally (the server stores only its hash), and every write is validated and
// rate-limited here. Setup: server/SETUP.md. Local testing: node server/dev_server.mjs (same code, SQLite file).
import { RULES, WORDS, PERKS, CARDS, BASES, MASCOTS, STATS, RATING, PERK_POWER, CARD_POWER } from "./generated.js";

const BOARDS = ["classic", "infinite"];
const LOWER_IS_BETTER = { classic: true, infinite: false };
const PERK_SET = new Set(PERKS), CARD_SET = new Set(CARDS), BASE_SET = new Set(BASES), MASCOT_SET = new Set(MASCOTS);
const INTRO_MAX = 60;

// ------------------------------------------------------------------ helpers

const json = (obj, status = 200) => new Response(JSON.stringify(obj), {
  status, headers: { "content-type": "application/json", "access-control-allow-origin": "*" },
});
const fail = (error, status = 400) => json({ ok: false, error }, status);

async function sha256hex(text) {
  const d = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(d)].map(b => b.toString(16).padStart(2, "0")).join("");
}

// a player's public id: the first 20 hex digits of the SHA-256 of their secret
async function playerOf(body) {
  const s = String(body.secret || "");
  if (!/^[0-9a-f]{64}$/.test(s)) throw new UserError("missing or malformed player secret");
  return (await sha256hex(s)).slice(0, 20);
}

class UserError extends Error { constructor(message, status = 400) { super(message); this.status = status; } }

// the mod's word filter: letter swaps, short words whole only
const SWAPS = { "0": "o", "1": "i", "3": "e", "4": "a", "5": "s", "7": "t", "@": "a", "$": "s" };
export function blocked(text) {
  const t = String(text || "").toLowerCase().replace(/[013457@$]/g, c => SWAPS[c]);
  const words = t.split(/[^a-z]+/).filter(Boolean);
  const squashed = t.replace(/[^a-z]/g, "");
  return WORDS.some(w => w.length <= 4 ? words.includes(w) : squashed.includes(w));
}

function cleanName(name, max) {
  const n = String(name || "").replace(/[\u0000-\u001f<>]/g, "").trim();
  if (n.length < RULES.name_min || n.length > max) throw new UserError(`names are ${RULES.name_min}-${max} characters`);
  if (blocked(n)) throw new UserError("that name isn't allowed");
  return n;
}

const boardVersion = mod => { const m = /^(\d+)\.(\d+)/.exec(String(mod || "")); if (!m) throw new UserError("bad mod version"); return `${m[1]}.${m[2]}`; };

// rate limits: events per key in a window
async function limit(env, key, max, seconds) {
  const now = Date.now();
  const row = await env.DB.prepare("SELECT COUNT(*) AS n FROM rate WHERE key = ? AND at > ?").bind(key, now - seconds * 1000).first();
  if ((row?.n || 0) >= max) throw new UserError("too many requests: try again later", 429);
  await env.DB.prepare("INSERT INTO rate (key, at) VALUES (?, ?)").bind(key, now).run();
  if (Math.random() < 0.02) await env.DB.prepare("DELETE FROM rate WHERE at < ?").bind(now - 2 * 86400000).run();
}

// ------------------------------------------------------------------ run logs (cheat checks)

// "slot,playerPoints,opponentPoints,seconds,opponent;..." one entry per match
export function parseLog(log) {
  return String(log || "").split(";").filter(Boolean).map(e => {
    const [slot, p, o, sec, ...opp] = e.split(",");
    return { slot: +slot, p: +p, o: +o, sec: +sec, opp: opp.join(",") };
  });
}

export function checkScore(board, value, log) {
  const m = parseLog(log);
  const R = RULES;
  if (!m.every(x => Number.isFinite(x.slot) && Number.isFinite(x.p) && Number.isFinite(x.o) && Number.isFinite(x.sec))) return "the run log is unreadable";
  const total = m.reduce((a, x) => a + x.sec, 0);
  if (board === "classic") {
    if (!(value >= R.classic_min_seconds && value <= R.classic_max_seconds)) return "impossible time";
    for (let g = 1; g <= 8; g++) {
      const x = m.find(e => e.slot === g);
      if (!x) return `match ${g} is missing from the run log`;
      if (!(x.p > x.o)) return `match ${g} wasn't won`;
      if (x.sec < R.match_min_seconds) return `match ${g} is impossibly short`;
    }
    if (total > value + 5) return "the matches take longer than the run";
    return null;
  }
  if (board === "infinite") {
    if (!(Number.isInteger(value) && value >= 1 && value <= R.infinite_max_rounds)) return "impossible round count";
    const wins = m.filter(x => x.p > x.o);
    if (wins.length !== value) return "the run log doesn't match the round count";
    if (wins.some(x => x.sec < R.match_min_seconds)) return "a match is impossibly short";
    if (total < value * R.infinite_seconds_per_round) return "too many rounds for the time played";
    return null;
  }
  return "unknown board";
}

// ------------------------------------------------------------------ tournament codes

const b64urlToBytes = s => {
  const bin = atob(s.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((s.length + 3) % 4));
  return Uint8Array.from(bin, c => c.charCodeAt(0));
};

async function inflate(bytes) {
  const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream("deflate-raw"));
  return new TextDecoder().decode(await new Response(stream).arrayBuffer());
}

// OVT<format>.<content>.<verify>.<check>; returns { content (parsed), contentB64, verify (parsed or null), hash }
export async function decodeCode(code) {
  const parts = String(code || "").trim().split(".");
  if (parts.length !== 4 || !/^OVT\d+$/.test(parts[0])) throw new UserError("not a tournament code");
  if (code.length > RULES.code_max_chars) throw new UserError("the code is too long");
  const [, content, verify, check] = parts;
  const sum = (await sha256hex(`${parts[0]}.${content}.${verify}`)).slice(0, 8);
  if (sum !== check) throw new UserError("the code is damaged (checksum)");
  let t, v = null;
  try { t = JSON.parse(await inflate(b64urlToBytes(content))); } catch { throw new UserError("the code is damaged"); }
  if (verify) { try { v = JSON.parse(await inflate(b64urlToBytes(verify))); } catch { throw new UserError("the code's clear record is damaged"); } }
  return { content: t, contentB64: content, verify: v, hash: (await sha256hex(content)).slice(0, 16) };
}

const hex6 = s => /^[0-9A-Fa-f]{6}$/.test(String(s));
const levelsOk = a => Array.isArray(a) && a.length === STATS.length && a.every(l => Number.isInteger(l) && l >= 0 && l <= RULES.stat_max_level);
const listOk = (a, set, max, unique) => Array.isArray(a) && a.length <= max && a.every(x => set.has(x)) && (!unique || new Set(a).size === a.length);

// the same checks as the game's (src/Core/Tournaments.cs Problem); keys as Tournaments.ToJson writes them
export function checkTournament(t) {
  const R = RULES;
  if (!t || typeof t !== "object") return "empty tournament";
  if (t.v !== 1) return `made with a newer Overtime (format ${t.v})`;
  if (typeof t.name !== "string" || !t.name.trim() || t.name.length > R.tournament_name_max || blocked(t.name)) return "the tournament name isn't allowed";
  if (typeof (t.desc ?? "") !== "string" || (t.desc || "").length > R.description_max || blocked(t.desc)) return "the description isn't allowed";
  if (t.mode !== "fixed" && t.mode !== "draft") return "unknown tournament type";
  const teams = Array.isArray(t.teams) ? t.teams : [];
  if (teams.length < R.tournament_min_matches || teams.length > R.tournament_max_matches) return "wrong number of matches";
  for (const [i, tm] of teams.entries()) {
    const n = `match ${i + 1}`;
    if (!tm || typeof tm.name !== "string" || !tm.name.trim() || tm.name.length > R.team_name_max || blocked(tm.name)) return `${n}: the team name isn't allowed`;
    if (typeof (tm.intro ?? "") !== "string" || (tm.intro || "").length > INTRO_MAX || blocked(tm.intro)) return `${n}: the team's line isn't allowed`;
    if (!BASE_SET.has(tm.base)) return `${n}: unknown squad`;
    if (![tm.jersey, tm.shorts, tm.banner].every(hex6) || !Array.isArray(tm.hair) || tm.hair.length !== 2 || !tm.hair.every(hex6)) return `${n}: bad colours`;
    const e = tm.emblem || {};
    if (!MASCOT_SET.has(e.mascot) || !["top", "stacked", "split"].includes(e.layout) || !["marker", "bangers"].includes(e.font) || !hex6(e.accent)) return `${n}: bad emblem`;
    if (!levelsOk(tm.levels)) return `${n}: bad stat levels`;
    if (!listOk(tm.perks, PERK_SET, R.team_max_perks, true)) return `${n}: unknown or too many perks`;
    if (!listOk(tm.cards, CARD_SET, R.team_max_cards, false)) return `${n}: unknown or too many stat cards`;
  }
  if (t.mode === "fixed") {
    const p = t.player || {};
    if (!levelsOk(p.levels)) return "the player's stat levels are bad";
    if (!listOk(p.perks, PERK_SET, R.loadout_max_perks, true)) return "the player's perks are unknown or too many";
    if (!listOk(p.cards, CARD_SET, R.loadout_max_cards, false)) return "the player's stat cards are unknown or too many";
  }
  return null;
}

// the difficulty 1-10 from the simulator-fitted rating (same formula as Tournaments.WinChance / Difficulty)
export function difficultyOf(t) {
  const sum = a => (a || []).reduce((x, y) => x + y, 0);
  const pp = a => sum((a || []).map(id => PERK_POWER[id] || 0)), cp = a => sum((a || []).map(id => CARD_POWER[id] || 0));
  let clear = 1;
  t.teams.forEach((tm, i) => {
    let pl, ppow, cpow;
    if (t.mode === "fixed") { pl = sum(t.player.levels); ppow = pp(t.player.perks); cpow = cp(t.player.cards); }
    else { pl = 0; ppow = Math.floor((i + 1) / 2) * RATING.draft_pick; cpow = Math.floor(i / 2) * RATING.draft_pick; }
    const logit = RATING.intercept + (RATING.bases[tm.base] || 0) + RATING.team_levels * sum(tm.levels) + RATING.team_perks * pp(tm.perks) + RATING.team_cards * cp(tm.cards)
      + RATING.player_levels * pl + RATING.player_perks * ppow + RATING.player_cards * cpow;
    clear *= 1 / (1 + Math.exp(-logit));
  });
  return Math.max(1, Math.min(10, Math.round(1 + 4.5 * -Math.log10(Math.max(1e-4, clear)))));
}

// the creator's own clear, attached to the code: every match won, plausible times
export function checkVerify(t, v, hash) {
  if (!v) return "the creator hasn't beaten this tournament yet";
  if (v.hash !== hash) return "the clear record belongs to another version of this tournament";
  const m = parseLog(v.log);
  for (let g = 1; g <= t.teams.length; g++) {
    const x = m.find(e => e.slot === g);
    if (!x || !(x.p > x.o) || x.sec < RULES.match_min_seconds) return "the clear record is incomplete";
  }
  return null;
}

// ------------------------------------------------------------------ routes

async function route(request, env) {
  const url = new URL(request.url);
  const path = url.pathname.replace(/\/+$/, "");
  const q = url.searchParams;
  const ip = request.headers.get("cf-connecting-ip") || request.headers.get("x-forwarded-for") || "local";
  const body = request.method === "POST" ? await request.json().catch(() => { throw new UserError("bad request body"); }) : {};
  if (request.method === "POST") await limit(env, "ip:" + ip, RULES.requests_per_ip_hour, 3600);

  if (path === "/v1/health") return json({ ok: true, server: "overtime", time: Date.now() });

  // ---- names
  if (path === "/v1/player" && request.method === "POST") {
    const id = await playerOf(body);
    const name = cleanName(body.name, RULES.name_max);
    await env.DB.prepare("INSERT INTO players (id, name, created) VALUES (?, ?, ?) ON CONFLICT(id) DO UPDATE SET name = excluded.name").bind(id, name, Date.now()).run();
    return json({ ok: true, id, name });
  }

  // ---- leaderboards
  if (path === "/v1/scores" && request.method === "POST") {
    const id = await playerOf(body);
    const board = String(body.board);
    if (!BOARDS.includes(board)) throw new UserError("unknown board");
    await limit(env, "score:" + id, RULES.scores_per_hour, 3600);
    const name = cleanName(body.name, RULES.name_max);
    const version = boardVersion(body.mod);
    const value = Number(body.value);
    const why = checkScore(board, value, body.log);
    if (why) throw new UserError("result rejected: " + why);
    await env.DB.prepare("INSERT INTO players (id, name, created) VALUES (?, ?, ?) ON CONFLICT(id) DO UPDATE SET name = excluded.name").bind(id, name, Date.now()).run();
    const old = await env.DB.prepare("SELECT value FROM scores WHERE board = ? AND version = ? AND player = ?").bind(board, version, id).first();
    const better = !old || (LOWER_IS_BETTER[board] ? value < old.value : value > old.value);
    if (better)
      await env.DB.prepare("INSERT INTO scores (board, version, player, value, date, mod, log, seed) VALUES (?, ?, ?, ?, ?, ?, ?, ?) " +
        "ON CONFLICT(board, version, player) DO UPDATE SET value = excluded.value, date = excluded.date, mod = excluded.mod, log = excluded.log, seed = excluded.seed")
        .bind(board, version, id, value, Date.now(), String(body.mod).slice(0, 16), String(body.log).slice(0, 4000), String(body.seed || "").slice(0, 64)).run();
    const rank = await rankOf(env, board, version, better ? value : old.value);
    return json({ ok: true, best: better, rank, value: better ? value : old.value });
  }
  if (path === "/v1/scores" && request.method === "GET") {
    const board = q.get("board"), version = q.get("version");
    if (!BOARDS.includes(board) || !/^\d+\.\d+$/.test(version || "")) throw new UserError("unknown board or version");
    const order = LOWER_IS_BETTER[board] ? "ASC" : "DESC";
    const rows = (await env.DB.prepare(`SELECT s.player, p.name, s.value, s.date, s.mod FROM scores s JOIN players p ON p.id = s.player ` +
      `WHERE s.board = ? AND s.version = ? AND s.flagged = 0 ORDER BY s.value ${order}, s.date ASC LIMIT ?`).bind(board, version, RULES.top_entries).all()).results;
    const out = { ok: true, board, version, ranks: [], ids: [], names: [], values: [], dates: [], mods: [], meRank: 0, meValue: 0, meDate: 0, total: 0 };
    rows.forEach((r, i) => { out.ranks.push(i + 1); out.ids.push(r.player); out.names.push(r.name); out.values.push(r.value); out.dates.push(r.date); out.mods.push(r.mod); });
    out.total = (await env.DB.prepare("SELECT COUNT(*) AS n FROM scores WHERE board = ? AND version = ? AND flagged = 0").bind(board, version).first()).n;
    const me = q.get("player");
    if (me && /^[0-9a-f]{20}$/.test(me)) {
      const mine = await env.DB.prepare("SELECT value, date FROM scores WHERE board = ? AND version = ? AND player = ? AND flagged = 0").bind(board, version, me).first();
      if (mine) { out.meValue = mine.value; out.meDate = mine.date; out.meRank = await rankOf(env, board, version, mine.value); }
    }
    return json(out);
  }

  // ---- tournaments
  const tm = /^\/v1\/tournaments(?:\/([A-Za-z0-9]{8}))?(?:\/(result|scores|rate|report|delete))?$/.exec(path);
  if (tm) return tournaments(request, env, body, q, tm[1], tm[2]);

  return fail("not found", 404);
}

async function rankOf(env, board, version, value) {
  const op = LOWER_IS_BETTER[board] ? "<" : ">";
  return (await env.DB.prepare(`SELECT COUNT(*) AS n FROM scores WHERE board = ? AND version = ? AND flagged = 0 AND value ${op} ?`).bind(board, version, value).first()).n + 1;
}

function newId() {
  const abc = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  return Array.from(crypto.getRandomValues(new Uint8Array(8)), b => abc[b % abc.length]).join("");
}

const LIST_ORDER = { new: "t.created DESC", played: "t.plays DESC, t.created DESC", hard: "t.difficulty DESC, t.created DESC", easy: "t.difficulty ASC, t.created DESC", liked: "(t.rating_sum * 1.0 / MAX(t.rating_n, 1)) DESC, t.rating_n DESC" };

async function tournaments(request, env, body, q, id, action) {
  const method = request.method;
  // publish / update
  if (!action && method === "POST") {
    const owner = await playerOf(body);
    const name = cleanName(body.name, RULES.name_max);
    const d = await decodeCode(body.code);
    const why = checkTournament(d.content) || checkVerify(d.content, d.verify, d.hash);
    if (why) throw new UserError(why);
    const difficulty = difficultyOf(d.content);
    await env.DB.prepare("INSERT INTO players (id, name, created) VALUES (?, ?, ?) ON CONFLICT(id) DO UPDATE SET name = excluded.name").bind(owner, name, Date.now()).run();
    if (id) {
      const row = await env.DB.prepare("SELECT owner FROM tournaments WHERE id = ?").bind(id).first();
      if (!row) throw new UserError("no such tournament", 404);
      if (row.owner !== owner) throw new UserError("only its creator can change this tournament", 403);
      await env.DB.prepare("UPDATE tournaments SET name = ?, description = ?, mode = ?, matches = ?, difficulty = ?, code = ?, hash = ?, updated = ?, mod = ? WHERE id = ?")
        .bind(d.content.name, d.content.desc || "", d.content.mode, d.content.teams.length, difficulty, body.code, d.hash, Date.now(), String(body.mod || "").slice(0, 16), id).run();
      return json({ ok: true, id, updated: true });
    }
    await limit(env, "publish:" + owner, RULES.publishes_per_day, 86400);
    const dupe = await env.DB.prepare("SELECT id FROM tournaments WHERE hash = ?").bind(d.hash).first();
    if (dupe) return json({ ok: true, id: dupe.id, existing: true });
    const nid = newId();
    await env.DB.prepare("INSERT INTO tournaments (id, owner, name, description, mode, matches, difficulty, code, hash, created, updated, mod) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)")
      .bind(nid, owner, d.content.name, d.content.desc || "", d.content.mode, d.content.teams.length, difficulty, body.code, d.hash, Date.now(), Date.now(), String(body.mod || "").slice(0, 16)).run();
    return json({ ok: true, id: nid });
  }
  // list
  if (!id && method === "GET") {
    const sort = LIST_ORDER[q.get("sort")] ? q.get("sort") : "new";
    const mode = ["fixed", "draft"].includes(q.get("mode")) ? q.get("mode") : null;
    const offset = Math.max(0, Math.min(10000, parseInt(q.get("offset") || "0", 10) || 0));
    const mine = /^[0-9a-f]{20}$/.test(q.get("owner") || "") ? q.get("owner") : null;
    const where = ["t.hidden = 0"]; const args = [];
    if (mode) { where.push("t.mode = ?"); args.push(mode); }
    if (mine) { where.push("t.owner = ?"); args.push(mine); }
    const rows = (await env.DB.prepare(`SELECT t.id, t.name, p.name AS creator, t.owner, t.mode, t.matches, t.difficulty, t.plays, t.clears, t.rating_sum, t.rating_n, t.created FROM tournaments t ` +
      `JOIN players p ON p.id = t.owner WHERE ${where.join(" AND ")} ORDER BY ${LIST_ORDER[sort]} LIMIT ? OFFSET ?`).bind(...args, RULES.page_size, offset).all()).results;
    const out = { ok: true, ids: [], names: [], creators: [], owners: [], modes: [], matches: [], difficulty: [], plays: [], clears: [], rating: [], ratings: [], created: [] };
    for (const r of rows) {
      out.ids.push(r.id); out.names.push(r.name); out.creators.push(r.creator); out.owners.push(r.owner); out.modes.push(r.mode); out.matches.push(r.matches);
      out.difficulty.push(r.difficulty); out.plays.push(r.plays); out.clears.push(r.clears); out.rating.push(r.rating_n ? r.rating_sum / r.rating_n : 0); out.ratings.push(r.rating_n); out.created.push(r.created);
    }
    return json(out);
  }
  const t = id ? await env.DB.prepare("SELECT * FROM tournaments WHERE id = ?").bind(id).first() : null;
  if (!t) throw new UserError("no such tournament", 404);
  // one
  if (!action && method === "GET") {
    const creator = (await env.DB.prepare("SELECT name FROM players WHERE id = ?").bind(t.owner).first())?.name || "?";
    return json({ ok: true, id: t.id, name: t.name, description: t.description, creator, owner: t.owner, mode: t.mode, matches: t.matches, difficulty: t.difficulty,
      plays: t.plays, clears: t.clears, rating: t.rating_n ? t.rating_sum / t.rating_n : 0, ratings: t.rating_n, code: t.code, hidden: t.hidden });
  }
  if (action === "delete" && method === "POST") {
    const owner = await playerOf(body);
    if (t.owner !== owner) throw new UserError("only its creator can delete this tournament", 403);
    await env.DB.batch([env.DB.prepare("DELETE FROM tournaments WHERE id = ?").bind(id), env.DB.prepare("DELETE FROM tplays WHERE tid = ?").bind(id),
      env.DB.prepare("DELETE FROM tratings WHERE tid = ?").bind(id), env.DB.prepare("DELETE FROM treports WHERE tid = ?").bind(id)]);
    return json({ ok: true, deleted: true });
  }
  // a finished attempt: plays, clears, fastest clears
  if (action === "result" && method === "POST") {
    const player = await playerOf(body);
    await limit(env, "result:" + player, RULES.results_per_hour, 3600);
    const name = cleanName(body.name, RULES.name_max);
    const cleared = !!body.cleared;
    const seconds = Number(body.seconds) || 0;
    if (cleared) {
      const m = parseLog(body.log);
      for (let g = 1; g <= t.matches; g++) {
        const x = m.find(e => e.slot === g);
        if (!x || !(x.p > x.o) || x.sec < RULES.match_min_seconds) throw new UserError("result rejected: the run log is incomplete");
      }
      if (seconds < t.matches * RULES.match_min_seconds || m.reduce((a, x) => a + x.sec, 0) > seconds + 5) throw new UserError("result rejected: impossible time");
    }
    await env.DB.prepare("INSERT INTO players (id, name, created) VALUES (?, ?, ?) ON CONFLICT(id) DO UPDATE SET name = excluded.name").bind(player, name, Date.now()).run();
    const old = await env.DB.prepare("SELECT cleared, best, attempts FROM tplays WHERE tid = ? AND player = ?").bind(id, player).first();
    const best = cleared ? (old && old.best > 0 ? Math.min(old.best, seconds) : seconds) : (old?.best || 0);
    await env.DB.prepare("INSERT INTO tplays (tid, player, cleared, best, attempts, date) VALUES (?, ?, ?, ?, 1, ?) ON CONFLICT(tid, player) DO UPDATE SET " +
      "cleared = MAX(cleared, excluded.cleared), best = ?, attempts = attempts + 1, date = excluded.date").bind(id, player, cleared ? 1 : 0, best, Date.now(), best).run();
    await env.DB.prepare(`UPDATE tournaments SET plays = plays + ?, clears = clears + ? WHERE id = ?`).bind(old ? 0 : 1, cleared && !(old && old.cleared) ? 1 : 0, id).run();
    return json({ ok: true, best });
  }
  if (action === "scores" && method === "GET") {
    const rows = (await env.DB.prepare("SELECT p.player, n.name, p.best, p.date FROM tplays p JOIN players n ON n.id = p.player WHERE p.tid = ? AND p.cleared = 1 AND p.best > 0 ORDER BY p.best ASC LIMIT ?")
      .bind(id, RULES.top_entries).all()).results;
    const out = { ok: true, ranks: [], ids: [], names: [], values: [], dates: [], meRank: 0, meValue: 0 };
    rows.forEach((r, i) => { out.ranks.push(i + 1); out.ids.push(r.player); out.names.push(r.name); out.values.push(r.best); out.dates.push(r.date); });
    const me = q.get("player");
    if (me && /^[0-9a-f]{20}$/.test(me)) {
      const mine = await env.DB.prepare("SELECT best FROM tplays WHERE tid = ? AND player = ? AND cleared = 1").bind(id, me).first();
      if (mine) { out.meValue = mine.best; out.meRank = (await env.DB.prepare("SELECT COUNT(*) AS n FROM tplays WHERE tid = ? AND cleared = 1 AND best > 0 AND best < ?").bind(id, mine.best).first()).n + 1; }
    }
    return json(out);
  }
  if (action === "rate" && method === "POST") {
    const player = await playerOf(body);
    const stars = Math.round(Number(body.stars));
    if (!(stars >= 1 && stars <= 5)) throw new UserError("ratings are 1 to 5 stars");
    const played = await env.DB.prepare("SELECT 1 AS y FROM tplays WHERE tid = ? AND player = ?").bind(id, player).first();
    if (!played) throw new UserError("play it before rating it");
    const old = await env.DB.prepare("SELECT stars FROM tratings WHERE tid = ? AND player = ?").bind(id, player).first();
    await env.DB.prepare("INSERT INTO tratings (tid, player, stars) VALUES (?, ?, ?) ON CONFLICT(tid, player) DO UPDATE SET stars = excluded.stars").bind(id, player, stars).run();
    await env.DB.prepare("UPDATE tournaments SET rating_sum = rating_sum + ?, rating_n = rating_n + ? WHERE id = ?").bind(stars - (old?.stars || 0), old ? 0 : 1, id).run();
    return json({ ok: true });
  }
  if (action === "report" && method === "POST") {
    const player = await playerOf(body);
    await limit(env, "report:" + player, RULES.reports_per_day, 86400);
    const reason = String(body.reason || "").slice(0, 200);
    const fresh = (await env.DB.prepare("INSERT OR IGNORE INTO treports (tid, player, reason, at) VALUES (?, ?, ?, ?)").bind(id, player, reason, Date.now()).run()).meta.changes;
    if (fresh) await env.DB.prepare("UPDATE tournaments SET reports = reports + 1, hidden = CASE WHEN reports + 1 >= ? THEN 1 ELSE hidden END WHERE id = ?").bind(RULES.reports_to_hide, id).run();
    return json({ ok: true });
  }
  return fail("not found", 404);
}

export default {
  async fetch(request, env) {
    try { return await route(request, env); }
    catch (e) {
      if (e instanceof UserError) return fail(e.message, e.status || 400);
      return fail("server error: " + (e && e.message || e), 500);
    }
  },
};


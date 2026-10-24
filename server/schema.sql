-- Hangtime! Overtime community server (Cloudflare D1 / SQLite). Apply with:
--   npx wrangler d1 execute overtime --remote --file=server/schema.sql
CREATE TABLE IF NOT EXISTS players (id TEXT PRIMARY KEY, name TEXT NOT NULL, created INTEGER);

-- one best result per player, board and board version (major.minor mod version)
CREATE TABLE IF NOT EXISTS scores (
  board TEXT NOT NULL, version TEXT NOT NULL, player TEXT NOT NULL,
  value REAL NOT NULL, date INTEGER, mod TEXT, log TEXT, seed TEXT, flagged INTEGER DEFAULT 0,
  PRIMARY KEY (board, version, player));
CREATE INDEX IF NOT EXISTS scores_rank ON scores (board, version, flagged, value);

-- rate limiting: one row per counted request
CREATE TABLE IF NOT EXISTS rate (key TEXT NOT NULL, at INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS rate_key ON rate (key, at);

CREATE TABLE IF NOT EXISTS tournaments (
  id TEXT PRIMARY KEY, owner TEXT NOT NULL, name TEXT, description TEXT, mode TEXT, matches INTEGER, difficulty REAL,
  code TEXT, hash TEXT, created INTEGER, updated INTEGER, mod TEXT,
  plays INTEGER DEFAULT 0, clears INTEGER DEFAULT 0, rating_sum INTEGER DEFAULT 0, rating_n INTEGER DEFAULT 0,
  reports INTEGER DEFAULT 0, hidden INTEGER DEFAULT 0);
CREATE INDEX IF NOT EXISTS tournaments_hash ON tournaments (hash);
CREATE TABLE IF NOT EXISTS tplays (tid TEXT, player TEXT, cleared INTEGER, best REAL, attempts INTEGER, date INTEGER, PRIMARY KEY (tid, player));
CREATE TABLE IF NOT EXISTS tratings (tid TEXT, player TEXT, stars INTEGER, PRIMARY KEY (tid, player));
CREATE TABLE IF NOT EXISTS treports (tid TEXT, player TEXT, reason TEXT, at INTEGER, PRIMARY KEY (tid, player));

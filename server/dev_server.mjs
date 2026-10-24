// Local test server: runs server/worker.js (the exact code Cloudflare runs) with a SQLite file standing in for D1.
//   node server/dev_server.mjs [port] [database file]      (default 8787, server/dev.sqlite)
// Then set [Community] Server = http://127.0.0.1:8787 in the mod's config. Needs Node 22.5+ (node:sqlite).
import http from "node:http";
import { readFileSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";
import { fileURLToPath } from "node:url";
import path from "node:path";
import worker from "./worker.js";

const here = path.dirname(fileURLToPath(import.meta.url));
const port = Number(process.argv[2] || 8787);
const db = new DatabaseSync(process.argv[3] || path.join(here, "dev.sqlite"));
db.exec(readFileSync(path.join(here, "schema.sql"), "utf8"));

// the slice of the D1 API the worker uses
class Statement {
  constructor(sql, args = []) { this.sql = sql; this.args = args; }
  bind(...args) { return new Statement(this.sql, args); }
  async first() { return db.prepare(this.sql).get(...this.args) ?? null; }
  async all() { return { results: db.prepare(this.sql).all(...this.args) }; }
  async run() { const r = db.prepare(this.sql).run(...this.args); return { meta: { changes: Number(r.changes) } }; }
}
const env = { DB: { prepare: sql => new Statement(sql), batch: async list => { for (const s of list) await s.run(); return []; } } };

http.createServer(async (req, res) => {
  const chunks = [];
  for await (const c of req) chunks.push(c);
  const request = new Request(`http://127.0.0.1:${port}${req.url}`, {
    method: req.method, headers: { ...req.headers, "cf-connecting-ip": req.socket.remoteAddress },
    body: ["GET", "HEAD"].includes(req.method) ? undefined : Buffer.concat(chunks),
  });
  const response = await worker.fetch(request, env);
  res.writeHead(response.status, Object.fromEntries(response.headers));
  res.end(Buffer.from(await response.arrayBuffer()));
  console.log(req.method, req.url, response.status);
}).listen(port, "127.0.0.1", () => console.log(`Overtime dev server on http://127.0.0.1:${port}`));

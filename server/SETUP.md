# Setting up the Overtime community server (leaderboards and shared tournaments)

The leaderboards and the community tournament browser need one small server that every player's game talks to.
It runs on **Cloudflare Workers** with a **D1** database. The free tier (100,000 requests a day, 5 GB of storage) is far
more than a mod's player count needs. You need a free Cloudflare account; no credit card.

Nothing in this folder is secret. The game ships **no key**: players identify themselves with a random secret their
own game keeps (the server stores only its hash), and the server checks and rate-limits every write itself.

## One-time setup (about 10 minutes)

1. Create a free account at <https://dash.cloudflare.com/sign-up> and verify the e-mail.
2. Install Node.js 18 or newer (<https://nodejs.org>), if it isn't installed already.
3. Open a terminal in this repo's `server` folder and log in:
   ```bash
   npx wrangler login
   ```
   (a browser window asks you to allow Wrangler, Cloudflare's own command-line tool)
4. Create the database:
   ```bash
   npx wrangler d1 create overtime
   ```
   It prints a `database_id`. Paste it into `server/wrangler.toml` in place of `PASTE-YOUR-DATABASE-ID-HERE`.
5. Create the tables:
   ```bash
   npx wrangler d1 execute overtime --remote --file=schema.sql
   ```
6. Put the server online:
   ```bash
   npx wrangler deploy
   ```
   It prints the address, like `https://overtime-community.<your-name>.workers.dev`.
7. Check it: open `<that address>/v1/health` in a browser. It should say `{"ok":true,"server":"overtime",...}`.
8. Tell Claude the address (or set it yourself): it goes in `src/Plugin.cs` as the default for `[Community] Server`,
   so every player's game uses it. Until then each player can paste it into `BepInEx/config/melty.hangtime.overtime.cfg`
   under `[Community]` → `Server = https://...`.

After that, the leaderboards and the tournament browser work for everyone with that version of the mod.

## Updating the server later

Edit `worker.js` (or regenerate `generated.js` with `python tools/sheets.py gen` after changing perks, cards or the
community sheet), then run `npx wrangler deploy` again. New tables or columns: add them to `schema.sql` and run step 5
again (the statements only create what's missing).

## Looking after it

- **See the leaderboard / remove a cheated score:**
  ```bash
  npx wrangler d1 execute overtime --remote --command "SELECT p.name, s.board, s.value, s.log FROM scores s JOIN players p ON p.id = s.player ORDER BY s.date DESC LIMIT 20"
  npx wrangler d1 execute overtime --remote --command "UPDATE scores SET flagged = 1 WHERE player = '<player id>'"
  ```
  A flagged score is hidden from the boards. Each score keeps its run log (every match's score and time) to check.
- **Reported tournaments:** three different players' reports hide a tournament until you look at it:
  ```bash
  npx wrangler d1 execute overtime --remote --command "SELECT t.id, t.name, t.description, r.reason FROM treports r JOIN tournaments t ON t.id = r.tid"
  npx wrangler d1 execute overtime --remote --command "UPDATE tournaments SET hidden = 0, reports = 0 WHERE id = '<id>'"   -- keep it
  npx wrangler d1 execute overtime --remote --command "DELETE FROM tournaments WHERE id = '<id>'"                       -- remove it
  ```
- **Usage:** the Cloudflare dashboard shows requests per day. A free account stops serving requests for the rest of the
  day at 100,000 (it doesn't start charging).

## Testing locally (no account needed)

```bash
node server/dev_server.mjs            # http://127.0.0.1:8787, data in server/dev.sqlite
```
Then set `Server = http://127.0.0.1:8787` in the mod's config. This runs the same `worker.js` with a local SQLite file.

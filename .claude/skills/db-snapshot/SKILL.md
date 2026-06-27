---
name: db-snapshot
description: Pull a fresh read-only snapshot of the production SQLite database from Railway down to prod.db in the repo root, for querying locally in DataGrip. Use when the user asks to refresh/pull/snapshot the prod DB, or runs /db-snapshot.
---

# db-snapshot

Pulls the live production SQLite file from the Railway persistent volume to `prod.db`
in the repo root. It's a **read-only point-in-time snapshot** — edits made locally do
**not** flow back to prod. Re-run any time for fresh data.

## Facts about this deployment

- Prod runs on **Railway** (service `clam-finance-tracker`, project linked via `railway link`).
- The DB is a plain **SQLite file on a Railway volume** mounted at `/data`.
- `DATABASE_URL=file:/data/prod.db` → the file lives at **`/data/prod.db`** (NOT under
  `server/prisma/` — the libSQL adapter resolves the path differently from Prisma CLI).
- The file is ~1 MB and contains **real personal financial data** — never commit it.
  `prod.db*` and `*.b64` are gitignored.

## Steps

1. **Run the download** from the repo root. Railway's `files download` is binary-safe —
   use it, do NOT try to stream the file through `railway ssh` + base64 (see Gotchas).

   **Always use the fully-qualified `railway.exe` path** (below) — never the bare `railway`
   command. The npm global bin is not reliably on PATH (especially in non-interactive shells),
   and using the full path every time avoids the "railway is not recognized" failure entirely.

   ```powershell
   $railway = "C:\Users\amackintosh\AppData\Roaming\npm\node_modules\@railway\cli\bin\railway.exe"
   Remove-Item prod.db, prod.db-wal, prod.db-shm, prod.db.b64 -ErrorAction SilentlyContinue
   & $railway volume files --volume "@helpdesk/server-volume" download /data/prod.db ./prod.db --overwrite
   ```

   The volume is **`@helpdesk/server-volume`** (mounted at `/data`); the remote path is
   `/data/prod.db`. The `--volume` flag sits on the `files` subcommand, NOT on `volume`.
   It may still print `> Select a volume @helpdesk/server-volume` but completes fine.

2. **Verify it landed whole** — expect a non-zero size around 966 KB (it grows over time):

   ```powershell
   (Get-Item prod.db).Length
   ```

   If the size is 0 or the command errored, see Gotchas. Do not report success on a 0-byte file.

3. **Sanity-check the contents** (optional but reassuring) with bun — no `sqlite3` CLI exists
   here, so use `bun:sqlite`:

   ```bash
   cd /c/Users/amackintosh/Source/repos/clam-finance-tracker
   cat > _verify_snap.mjs <<'EOF'
   import { Database } from "bun:sqlite";
   const db = new Database("prod.db", { readonly: true });
   console.log("Total transactions:", db.query('SELECT COUNT(*) AS n FROM "Transaction"').get().n);
   console.table(db.query('SELECT substr(description,1,30) AS description, date(date) AS date, datetime(createdAt) AS created FROM "Transaction" ORDER BY createdAt DESC LIMIT 5').all());
   db.close();
   EOF
   bun _verify_snap.mjs && rm -f _verify_snap.mjs
   ```

4. **Report** the local path (`prod.db`), its size, row count, and newest `createdAt` to the
   user. Remind them it's a read-only snapshot. **DataGrip caches the old connection** — after
   a fresh pull they must right-click the `prod - clamfinancetracker` data source → **Disconnect**,
   then re-query, or DataGrip keeps showing stale data from the previous file.

## Gotchas (learned the hard way)

- **`railway` not found:** the npm global bin is not reliably on PATH. This is why Step 1
  always invokes the full path `C:\Users\amackintosh\AppData\Roaming\npm\node_modules\@railway\cli\bin\railway.exe`
  via `& $railway`. If that path ever 404s, find the real one with
  `(Get-Command railway -ErrorAction SilentlyContinue).Source` in a fresh terminal, or
  `npm root -g` + `\@railway\cli\bin\railway.exe`.
- **Never base64-over-ssh.** `railway ssh -- sh -c "base64 ..."` piped/redirected returns
  0–6 bytes — `railway ssh` allocates a PTY (output goes to the terminal, bypassing the pipe),
  and feeding stdin to disable the PTY closes the session before a big file finishes. The
  `files download` command sidesteps all of this. This is the whole reason this skill exists.
- **CLI too old:** if `files download` is unrecognised, `npm i -g @railway/cli@latest` then retry.
- **SSH key:** `files download` uses the SSH key at `~/.ssh/id_ed25519`. If it complains about
  keys, `ssh-keygen -t ed25519` (accept defaults) once.
- **Not linked:** if it can't find the service, run `railway link` (pick project → production →
  service) from the repo dir, or pass `-s clam-finance-tracker`.

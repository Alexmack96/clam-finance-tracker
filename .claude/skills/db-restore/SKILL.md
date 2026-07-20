---
name: db-restore
description: Overwrite the local dev SQLite database with a fresh copy of production data pulled from Railway. Use when the user asks to refresh/restore/sync dev with prod, or runs /db-restore.
---

# db-restore

Downloads the live production SQLite file from the Railway volume and overwrites
**`server/dev.db`** (the real local dev database) with it. Unlike `db-snapshot` (which
only pulls a read-only copy to `prod.db` in the repo root for DataGrip), this skill
replaces your actual dev data — the local database `bun run dev` reads from.

This is one-way: prod → dev. Local dev changes are never pushed back to prod.

## Facts about this deployment

- Prod runs on **Railway** (service `clam-finance-tracker`, project linked via `railway link`).
- The DB is a plain **SQLite file on a Railway volume** mounted at `/data`, remote path
  `/data/prod.db`.
- **The real local dev DB is `server/dev.db`** (~1.3 MB), NOT `server/prisma/dev.db` or the
  root-level `dev.db` — those two are decoys: empty (0-byte) files that happen to be tracked
  in git too. Confirm by checking `DATABASE_URL` in the repo-root `.env` (`file:./dev.db`) —
  the `server` package's `dev` script runs with cwd `server/`, so it resolves to `server/dev.db`.
- `server/dev.db` **is tracked in git** (unusual, but true in this repo) — that's actually a
  safety net: if a restore goes wrong, `git checkout -- server/dev.db` reverts it.
- The prod file contains **real personal financial data** — never commit the intermediate
  snapshot. `prod.db*` is gitignored.

## Steps

1. **Check for a running dev server.** If `bun run dev` / `bun --watch` is active, it holds
   `server/dev.db` open and won't see the new data until restarted. This doesn't block the
   overwrite, but warn the user to restart it afterward.

   ```powershell
   Get-Process bun,node -ErrorAction SilentlyContinue
   ```

2. **Download prod to a temp file** — from the repo root, in **PowerShell, not Bash**. Git
   Bash mangles the leading `/data/...` remote path (MSYS path translation rewrites it to a
   Windows path and the download 404s) — this must run as PowerShell.

   ```powershell
   Set-Location "c:\Users\amackintosh\Source\repos\clam-finance-tracker"
   $railway = "C:\Users\amackintosh\AppData\Roaming\npm\node_modules\@railway\cli\bin\railway.exe"
   Remove-Item prod.db, prod.db-wal, prod.db-shm -ErrorAction SilentlyContinue
   & $railway volume files --volume "@helpdesk/server-volume" download /data/prod.db ./prod.db --overwrite
   ```

   It may print `> Select a volume @helpdesk/server-volume` — that's normal, it still completes.

3. **Verify it landed whole** before touching dev.db — expect a non-zero size (~1+ MB, grows
   over time). Do not proceed to step 4 on a 0-byte or missing file.

   ```powershell
   (Get-Item prod.db).Length
   ```

4. **Sanity-check the contents** with `bun:sqlite` (no `sqlite3` CLI here):

   ```bash
   cd /c/Users/amackintosh/Source/repos/clam-finance-tracker
   cat > _verify_snap.mjs <<'EOF'
   import { Database } from "bun:sqlite";
   const db = new Database("prod.db", { readonly: true });
   console.log("Total transactions:", db.query('SELECT COUNT(*) AS n FROM "Transaction"').get().n);
   db.close();
   EOF
   bun _verify_snap.mjs && rm -f _verify_snap.mjs
   ```

5. **Overwrite `server/dev.db`** and drop any stale WAL sidecar files so SQLite doesn't try to
   replay an old journal against the new file:

   ```bash
   cp prod.db server/dev.db
   rm -f server/dev.db-wal server/dev.db-shm
   ```

6. **Verify the copy landed** the same way as step 4, reading `server/dev.db` this time.

7. **Reset the local admin password.** The restored data carries prod's *real* password hash
   for the admin account, which silently breaks local login — `ADMIN_PASSWORD` in `.env` no
   longer matches what's actually stored. Always run this after every restore, no exceptions:

   ```bash
   cd server && bun scripts/reset-dev-admin-password.ts
   ```

   This resets the credential back to `ADMIN_EMAIL`/`ADMIN_PASSWORD` from `.env`, so local
   login keeps working with the same dev credential regardless of what prod's password is.

8. **Clean up** the temporary `prod.db`/`prod.db-wal`/`prod.db-shm` in the repo root — its
   contents now live in `server/dev.db`, no need to keep two copies of real financial data
   on disk:

   ```bash
   rm -f prod.db prod.db-wal prod.db-shm
   ```

9. **Report** to the user: row count restored, and remind them to **restart the dev server**
   if one was running (step 1). Mention `server/dev.db` is git-tracked, so `git status` will
   show it modified, and `git checkout -- server/dev.db` reverts if needed. Also confirm step 7
   ran — that's the step that keeps login working.

## Gotchas (learned the hard way)

- **Bash mangles the Railway path.** Step 2 must run in PowerShell — in Git Bash the remote
  path `/data/prod.db` gets MSYS-rewritten into a Windows path and the download fails with
  `Failed to stat remote path /data/C:/Program Files/...`.
- **`railway` not found:** the npm global bin isn't reliably on PATH. Always invoke the
  fully-qualified `railway.exe` path shown above via `& $railway`, never the bare `railway`
  command. If that path 404s, find the real one with
  `(Get-Command railway -ErrorAction SilentlyContinue).Source` in a fresh terminal, or
  `npm root -g` + `\@railway\cli\bin\railway.exe`.
- **Never base64-over-ssh** to fetch the file — `railway ssh` allocates a PTY that breaks
  piped/redirected output for large files. `volume files download` sidesteps this entirely.
- **Don't confuse the three "dev.db" files.** Only `server/dev.db` is real; `server/prisma/dev.db`
  and the root `dev.db` are empty tracked files and overwriting them does nothing useful.
- **CLI too old:** if `files download` is unrecognised, `npm i -g @railway/cli@latest` then retry.
- **Not linked:** if it can't find the service, run `railway link` (pick project → production →
  service) from the repo dir, or pass `-s clam-finance-tracker`.
- **Login breaks after restore if step 7 is skipped.** The admin `User` row comes over from
  prod with prod's real password hash on its `Account`. Trying to log in locally with the
  `.env` `ADMIN_PASSWORD` then fails with "Invalid email or password" — looks like a broken
  app, but it's just a stale local credential. `bun scripts/reset-dev-admin-password.ts` (from
  `server/`) fixes it; also runnable directly as `bun run db:reset-dev-password`.

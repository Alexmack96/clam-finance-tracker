---
name: file-snapshot
description: Pull the original statement PDFs from the Railway persistent volume down to the local archive at "C:\Users\amackintosh\Personal Finance\Statements". Use when the user asks to pull/sync/snapshot the statement PDFs or files, or runs /file-snapshot.
---

# file-snapshot

Sibling of `db-snapshot`. That one pulls `/data/prod.db`; this one pulls `/data/statements`
— the original uploaded PDFs — into the local archive at:

```
C:\Users\amackintosh\Personal Finance\Statements
```

**The local archive is additive.** Files are merged in, never deleted. If a statement is
removed from prod, the local copy survives — that's the point of an archive. Do NOT
`railway ... download --overwrite` straight onto the archive directory; `--overwrite`
replaces `LOCAL_PATH` wholesale and would take the archive with it. Stage, then merge.

## Facts about this deployment

- Prod runs on **Railway**, volume **`@helpdesk/server-volume`** mounted at `/data`.
- PDFs live at **`/data/statements`**, laid out by
  [`statementStore.keyFor`](../../../server/src/lib/statementStorage.ts):
  `<bank>/<owner>/<statement-date>-<hash12>.pdf` — e.g. `amex/Alex/March-2025-a1b2c3d4e5f6.pdf`.
- The filename is **not** the original upload name. The mapping lives in the `StatementFile`
  table (`storageKey` → `originalName`), which is why step 4 writes a manifest.
- `STATEMENTS_DIR` is **not** set in Railway and does not need to be — `defaultStatementsDir()`
  derives `/data/statements` from `DATABASE_URL=file:/data/prod.db`.
- These are **real personal financial documents**. They live outside the repo on purpose.
  Never copy them into the working tree.

## Steps

Always use the fully-qualified `railway.exe` path — the npm global bin is not reliably on
PATH in non-interactive shells.

1. **Stage the download** into a scratch dir (never straight into the archive):

   ```powershell
   $railway = "C:\Users\amackintosh\AppData\Roaming\npm\node_modules\@railway\cli\bin\railway.exe"
   $stage   = Join-Path $env:TEMP "statements-stage"
   Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
   & $railway volume files --volume "@helpdesk/server-volume" download /data/statements $stage --json
   ```

   It prints `> Select a volume @helpdesk/server-volume` and completes fine — that line is
   not a prompt. If it fails with `no such file or directory`, prod has never had a PDF
   uploaded (or the `statement_files` migration isn't deployed) — see Gotchas.

2. **Find the real source root.** Depending on CLI version the download lands either as
   `$stage\*` or `$stage\statements\*`. Normalise before merging:

   ```powershell
   $src = if (Test-Path (Join-Path $stage "statements")) { Join-Path $stage "statements" } else { $stage }
   (Get-ChildItem $src -Recurse -Filter *.pdf).Count
   ```

   If that count is 0, stop and investigate — do not report success.

3. **Merge into the archive.** `robocopy` copies new/changed files and leaves everything
   else alone (no `/MIR` — that would mirror deletions):

   ```powershell
   $archive = "C:\Users\amackintosh\Personal Finance\Statements"
   New-Item -ItemType Directory -Force $archive | Out-Null
   robocopy $src $archive /E /XO /NFL /NDL /NJH /NP
   if ($LASTEXITCODE -ge 8) { throw "robocopy failed: $LASTEXITCODE" }
   Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
   ```

   robocopy exit codes below 8 mean success (0 = nothing to copy, 1 = files copied, 3 =
   copied + extras present). Only `>= 8` is a real failure — do not treat 1 or 3 as an error.

4. **Write the manifest** so the hashed filenames are traceable back to original uploads.
   Needs a current `prod.db` in the repo root — run the `db-snapshot` skill first if it's
   stale. No `sqlite3` CLI exists here, so use `bun:sqlite`:

   ```bash
   cd /c/Users/amackintosh/Source/repos/clam-finance-tracker
   cat > _manifest.mjs <<'EOF'
   import { Database } from "bun:sqlite";
   import { writeFileSync } from "fs";
   const db = new Database("prod.db", { readonly: true });
   const rows = db.query(
     "SELECT storageKey, bank, owner, statementDate, originalName, byteSize, rowCount, substr(uploadedAt,1,10) AS uploadedAt FROM statement_file ORDER BY uploadedAt DESC"
   ).all();
   const esc = (v) => `"${String(v ?? "").replace(/"/g, '""')}"`;
   const csv = [
     "storageKey,bank,owner,statementDate,originalName,byteSize,rowCount,uploadedAt",
     ...rows.map((r) => Object.values(r).map(esc).join(",")),
   ].join("\n");
   writeFileSync("C:/Users/amackintosh/Personal Finance/Statements/manifest.csv", csv);
   console.log(`manifest: ${rows.length} statement files`);
   db.close();
   EOF
   bun _manifest.mjs && rm -f _manifest.mjs
   ```

   The Prisma model is `StatementFile` but the SQLite table is **`statement_file`**
   (`@@map`) — query the mapped name. If that table doesn't exist in `prod.db`, the
   migration isn't deployed — say so and skip this step rather than failing the whole run.

   `uploadedAt` is stored as an **ISO TEXT** string (`2026-07-26T20:05:31.196+00:00`), not
   epoch-ms — hence `substr(...,1,10)`. Using `date(uploadedAt/1000,'unixepoch')` silently
   yields `1970-01-01` for every row.

5. **Report**: PDF count now in the archive, how many were newly copied, total size, and the
   manifest path. Note that the archive is additive — nothing was deleted.

## Gotchas

- **`no such file or directory` on `/data/statements`** — the directory is created lazily by
  `statementStore.save()` on the first successful upload. If it's missing, either no PDF has
  been uploaded to prod, or the `20260726130000_statement_files` migration hasn't been
  deployed. Check with:
  ```powershell
  & $railway volume files --volume "@helpdesk/server-volume" list /data --json
  ```
  and check the prod snapshot's applied migrations:
  ```bash
  bun -e 'const {Database}=require("bun:sqlite");const d=new Database("prod.db",{readonly:true});console.log(d.query("SELECT migration_name FROM _prisma_migrations ORDER BY finished_at DESC LIMIT 3").all())'
  ```
- **`Failed to initialize SFTP session: Timeout`** — transient. Just re-run the same command;
  it succeeds on the second attempt. Don't go hunting for a config problem.
- **Never `--overwrite` onto the archive** — it replaces `LOCAL_PATH`, and the archive is
  meant to outlive prod. Stage + robocopy, as above.
- **Never base64-over-ssh.** `railway ssh` allocates a PTY, so piped binary output comes back
  truncated to a few bytes. `files download` is the only reliable path (same lesson as
  `db-snapshot`).
- **`railway` not found** — always invoke the full `railway.exe` path shown in step 1.
- **Not linked** — run `railway link` from the repo dir, or pass `-s clam-finance-tracker`.

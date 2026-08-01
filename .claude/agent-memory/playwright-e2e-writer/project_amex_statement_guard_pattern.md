---
name: Amex statement upload guard e2e pattern
description: How e2e/amex-statement-guard.spec.ts isolates on-disk PDF writes and simulates the "rows already staged, no matching StatementFile" case without a second real PDF
type: project
---

Coverage lives in `e2e/amex-statement-guard.spec.ts` for `POST /api/admin/import/amex` (server/src/routes/import.ts). Two separate 409s exist on that route — don't conflate them in test assertions:
1. contentHash guard (pre-existing): byte-identical re-upload, keyed on `StatementFile.contentHash`. Message starts "This exact PDF was already uploaded on ...".
2. all-rows-staged guard (added 2026-08-01): `partitionAmexRows` (pure, no writes) runs before any persistence; if every parsed row already exists in `amex_transaction`, responds 409 with "Every transaction on this statement (<n>) is already imported, so nothing was stored." and creates no `StatementFile` row / writes no PDF.

**Disk isolation:** added `STATEMENTS_DIR=./statements/e2e-test` to `server/.env.test` (nested under the already-gitignored `server/statements/` tree — no new gitignore entry needed). Without this override, `defaultStatementsDir()` (server/src/lib/statementStorage.ts) resolves to `server/statements` for BOTH dev (`.env`, `dev.db`) and test (`.env.test`, `test.db`) — the dirname computation only depends on the directory portion of `DATABASE_URL`, not the filename, so dev and test share a statements folder unless explicitly separated. Any future spec inspecting on-disk statement PDFs should reuse this same env var rather than the default.

**Simulating "different bytes, same rows already staged" without a second PDF:** upload the real fixture once for real (creates `StatementFile` + `amex_transaction` rows), then:
1. `UPDATE amex_transaction SET statementFileId = NULL WHERE statementFileId = '<id>'` — detach the FK so the rows survive.
2. `DELETE FROM statement_file WHERE id = '<id>'`.
3. Wipe the statements dir.

Re-uploading the *identical* bytes then finds no `contentHash` match (guard 1 stays quiet, since the row is gone) but every parsed row is still present in `amex_transaction` (guard 2 fires). This reproduces the "bytes differ, rows already staged" scenario without fabricating a second PDF with different bytes but identical content — the reliable route the task itself suggested (pre-stage via `assignAmexIds` before ever uploading), just derived by reusing the real upload+parse path instead of reimplementing the hashing algorithm in test code.

**Fixture:** `e2e/resources/statements/2026-07-24-amex.pdf` — reused from `server/src/routes/amex-statement.test.ts` (real statement, already proven to reconcile). Row count (`N`) is read dynamically from the first upload's `imported` field rather than hardcoded, so the spec doesn't drift if the fixture changes.

**Isolation/cleanup:** no other e2e spec touches Amex tables, so `beforeEach`/`afterAll` do a full wipe (`amex_transaction`, `statement_file WHERE bank='amex'`, `"Transaction" WHERE externalId LIKE 'amex:%'`) plus `rmSync(statementsDir, {recursive:true,force:true})` — no narrow `LIKE` filtering needed (contrast with [[project_e2e_conventions]]'s barclays-dedup pattern, which scopes narrowly because Barclays tables could plausibly be touched elsewhere).

**Why:** the new guard's whole point is "no side effect" — the test must assert absence (row count unchanged, PDF count unchanged), not just the 409 status code, or a regression that silently reintroduces the orphan-write bug would pass.
**How to apply:** reuse the STATEMENTS_DIR env var and the detach-then-delete trick for any future spec that needs to simulate stale/orphaned StatementFile linkage state.

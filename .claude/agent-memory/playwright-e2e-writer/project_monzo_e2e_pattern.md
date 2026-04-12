---
name: Monzo pipeline seeding and mocking pattern
description: How to seed MonzoCredential/MonzoApiTransaction and mock the status endpoint in e2e tests for the Monzo sync→stage→process pipeline
type: project
---

`server/.env.test` does not include `MONZO_CLIENT_ID`, `MONZO_CLIENT_SECRET`, or `MONZO_REDIRECT_URI`. This means the real `/api/admin/monzo/status` endpoint returns `configured: false`, which causes the Import page to show the config instructions card instead of the "Sync now" button.

**Fix:** mock `**/api/admin/monzo/status` via `page.route()` in every Monzo test to return `{ configured: true, connected: true, ... }`.

**Why:** Adding Monzo vars to .env.test is undesirable — they'd need to be real or the server would need extra stub logic. Client-side route mocking is cleaner.

**How to apply:** Call `mockMonzoConnected(page)` before `page.goto('/import')` in any test that needs to see the connected Monzo card.

**DB seeding:** Use `bun:sqlite` via `execSync('bun -e "..."')` to insert rows directly into the test SQLite DB. Resolve the DB path from `testEnv.DATABASE_URL` (strip `file:` prefix, resolve relative to `serverDir`). The test DB uses `file:./test.db` → `server/test.db`. Tables exist after global-setup runs `prisma migrate deploy`.

**Table names (mapped):**
- `monzo_credential` — credential row, `userId` is unique
- `monzo_api_transaction` — staged rows, `monzoId` is unique, `status` defaults to `"pending"`

**Process test pattern:** seed rows directly with `INSERT OR IGNORE`, skip the sync step entirely.

---
name: feedback_full_suite_sqlite_flakiness
description: Full e2e suite run in parallel workers is occasionally flaky against the shared test SQLite file — not necessarily a bug in the spec under test
type: feedback
---

Running `npx playwright test` (full suite, default parallel workers, no `workers: 1` in config) occasionally fails a handful of unrelated specs (`monzo-sync.spec.ts`, `users.spec.ts`, `auth.spec.ts` route-guard test) with timeouts, on a run where they'd pass individually or on a re-run. Observed while adding `e2e/transactions-rendering.spec.ts`, which does a large (1500-row) single-transaction bulk insert via `bun:sqlite` in `beforeAll` — plausible that a big write transaction briefly locks the shared `test.db` file and stalls concurrent workers' Prisma/bun:sqlite calls.

**Why:** Discovered when a full-suite run showed 6 failures including the new spec, but a second full-suite run right after passed the new spec cleanly while different specs failed instead — pointing to shared-DB contention under parallelism, not a deterministic bug in any one spec.

**How to apply:** Don't chase full-suite flakiness as if it were a bug in the spec you just wrote — first re-run in isolation (`npx playwright test e2e/<file>.spec.ts`) and re-run the full suite once or twice before concluding there's a real regression. If a spec does a large bulk seed, consider whether it's worth flagging to the user as a candidate for `workers: 1` or a dedicated test DB per worker — but don't unilaterally change global Playwright config to fix this without asking, since it affects every spec's run time.

**Also observed (2026-08-01):** `POST /api/admin/process` itself (not just Monzo-specific paths) can fail with Prisma `P2025`/`P1008` when 3 import spec files (`amex-statement-guard.spec.ts`, `barclays-dedup.spec.ts`, `monzo-sync.spec.ts`) run together under default parallel workers — passed cleanly in isolation and on immediate re-run of the same 3-file combo. Confirms the contention is at the shared `test.db` / webServer-boot level, not tied to any one spec's seeding.

## RESOLVED (2026-08-01) — and a warning about this note's earlier advice

Two separate problems were being lumped together under "flakiness":

1. **Real contention** (`amex-statement-guard`, `barclays-dedup`, `monzo-sync`). Confirmed
   mechanism, from Prisma's own source: the adapter maps `SQLITE_BUSY` → `SocketTimeout` →
   **P1008**. Fixed by setting `workers: 1` in `playwright.config.ts`. Every spec shares one
   server process and one `server/test.db`; per-worker isolation would need a database and a
   server per worker. Serialized costs ~25s on this suite.

2. **Not flaky at all** (`users.spec.ts`, `auth.spec.ts` `/users` route guard). These failed
   **100% of runs, parallel or serialized**, because `feat: remove admin` (cbee3a7, Jun 2026)
   deleted `UsersPage.tsx`, `UsersTable.tsx`, `CreateUserDialog.tsx` and the `/users` route.
   The tests outlived the feature by over a month. `users.spec.ts` was deleted; the auth route
   guard now targets `/import`.

**Correction to the guidance above:** "re-run before concluding there's a real regression" is
right, but it is not sufficient, and it was used here to wave away two deterministic failures.
A test that fails on *every* run is not flaky — before attributing anything to contention,
check whether it fails serialized (`--workers=1`). If it does, it's a real bug or a dead test.
Confirm the UI/route/element under test still exists in the app before blaming the harness.

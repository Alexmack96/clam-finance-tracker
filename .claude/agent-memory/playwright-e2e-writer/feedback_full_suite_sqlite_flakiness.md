---
name: feedback_full_suite_sqlite_flakiness
description: Full e2e suite run in parallel workers is occasionally flaky against the shared test SQLite file — not necessarily a bug in the spec under test
type: feedback
---

Running `npx playwright test` (full suite, default parallel workers, no `workers: 1` in config) occasionally fails a handful of unrelated specs (`monzo-sync.spec.ts`, `users.spec.ts`, `auth.spec.ts` route-guard test) with timeouts, on a run where they'd pass individually or on a re-run. Observed while adding `e2e/transactions-rendering.spec.ts`, which does a large (1500-row) single-transaction bulk insert via `bun:sqlite` in `beforeAll` — plausible that a big write transaction briefly locks the shared `test.db` file and stalls concurrent workers' Prisma/bun:sqlite calls.

**Why:** Discovered when a full-suite run showed 6 failures including the new spec, but a second full-suite run right after passed the new spec cleanly while different specs failed instead — pointing to shared-DB contention under parallelism, not a deterministic bug in any one spec.

**How to apply:** Don't chase full-suite flakiness as if it were a bug in the spec you just wrote — first re-run in isolation (`npx playwright test e2e/<file>.spec.ts`) and re-run the full suite once or twice before concluding there's a real regression. If a spec does a large bulk seed, consider whether it's worth flagging to the user as a candidate for `workers: 1` or a dedicated test DB per worker — but don't unilaterally change global Playwright config to fix this without asking, since it affects every spec's run time.

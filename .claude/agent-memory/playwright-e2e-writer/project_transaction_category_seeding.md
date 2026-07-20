---
name: project_transaction_category_seeding
description: How to bulk-seed Transaction/Category rows directly via bun:sqlite for /transactions (DashboardPage.tsx) e2e tests, and route naming gotcha
type: project
---

`/transactions` route renders `DashboardPage.tsx` (the AG Grid + mobile card transaction list). `/dashboard` renders `AnalyticsPage.tsx` instead — don't confuse the two when writing specs, the names are swapped from what you'd expect.

**Table names:** `Transaction` and `Category` models have no `@@map` in `schema.prisma`, so their actual SQLite table names are the literal `"Transaction"` / `"Category"` (capitalized) — unlike the bank staging tables (`monzo_api_transaction` etc., see [[project_monzo_e2e_pattern]]) which are snake_case via `@@map`. SQLite table name matching is case-insensitive so lowercase also works, but match the schema's casing for clarity.

**Columns with schema defaults can be omitted from INSERT** — SQLite fills them in: `owner` (default `Joint`), `reviewed`/`excludeFromSavings` (default `false`), `externalId`/`note`/`savingType` (nullable, default null), `createdAt` (default `CURRENT_TIMESTAMP`). Only `id`, `description`, `amount`, `type`, `date`, `categoryId` need explicit values for a minimal valid row. `categoryId` is a required FK — seed a `Category` row first (`INSERT OR IGNORE`, unique `name`).

**Bulk insert perf:** for ~1000+ rows, build the INSERT as a `db.prepare(...)` + `db.transaction((rows) => { for (...) insert.run(...) })` loop inside the bun script (not one giant string-interpolated SQL statement per row) — see `e2e/transactions-rendering.spec.ts` `seedTransactions()`.

**AG Grid DOM selectors for rendering assertions:**
- `.ag-row` — one per rendered (not total) row; count it to assert virtualisation is active (`domLayout="autoHeight"` would render ALL rows into `.ag-row`, breaking virtualisation).
- `.ag-root-wrapper` — the grid's root container; absent entirely (not just hidden) when the component isn't mounted. Use `toHaveCount(0)` to assert non-mount, not a visibility check — CSS-only `md:hidden`/`hidden md:block` gating still mounts the component and would pass a visibility-only assertion.

**Mobile/desktop tree gating:** this app uses `useIsDesktop()` (`client/src/lib/useIsDesktop.ts`, `matchMedia("(min-width: 768px)")`, `useSyncExternalStore`) to actually unmount the inactive tree — not CSS classes. When testing pages that use this pattern, use `page.setViewportSize()` + `toHaveCount(0)` on a marker unique to the *other* tree, in both directions, to prove real unmounting.

**Why:** Global setup only seeds the admin user (zero transactions/categories) — see [[project_e2e_conventions]] — so any spec needing transaction data must seed it itself and clean up in `afterAll`.
**How to apply:** Reuse this pattern for future specs touching `/transactions` or any other page gated by `useIsDesktop`.

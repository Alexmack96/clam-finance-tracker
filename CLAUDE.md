# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Express 5 Error Handling

Express 5 automatically forwards async errors to the error handler — no `try/catch` needed for generic propagation. Only use `try/catch` when mapping a specific error to a specific HTTP response (e.g. Prisma `P2002` → 409, CSV parse failure → 400). Never wrap async DB calls in `try/catch` just to `throw err` or call `next(err)`.

## Enums

Always use Prisma-generated enums (e.g. `UserRole.User`, `UserRole.Admin`) instead of hardcoding string literals. Import from `../generated/prisma/index.js`.

## Context7

Always use Context7 (`npx ctx7@latest library <name>` then `npx ctx7@latest docs <id>`) when working with any library, framework, or API in this codebase — Prisma, Express, React, Vite, Tailwind, Zod, Bun, Anthropic SDK, etc.

## Dev Commands

All commands use **Bun** as the runtime/package manager.

### Local development (two terminals)

```bash
# Terminal 1
cd server && bun run dev      # hot-reload via bun --watch

# Terminal 2
cd client && bun run dev      # Vite dev server on :5173
```

Or from repo root:

```bash
bun run dev
```

Database is **SQLite** (`server/prisma/dev.db`) — no external DB process needed.

### Server DB commands

```bash
bun run db:migrate:deploy  # apply existing migrations (use this — migrate dev is interactive)
bun run db:seed            # seed admin user only (no sample data)
bun run db:studio          # Prisma Studio GUI
bun run db:generate        # regenerate Prisma client after schema change
```

**Adding a migration:** `prisma migrate dev` requires an interactive TTY. Instead:
1. Write the SQL manually in `server/prisma/migrations/<timestamp>_<name>/migration.sql`
2. Run `bun run db:migrate:deploy` to apply it
3. Run `bun run db:generate` to regenerate the client

### Lint

```bash
cd server && bun run lint
cd client && bun run lint
```

### Component tests (client)

```bash
cd client && bun run test       # Vitest watch mode
cd client && bunx vitest run    # single run
```

Tests use Vitest + React Testing Library. Setup file: `client/src/test/setup.ts`. Shared helper: `client/src/test/renderWithQuery.tsx`.

### E2E Tests

```bash
npx playwright test          # run all e2e tests
npx playwright test --ui     # interactive UI mode
npx playwright show-report   # view last test report
```

Use the **playwright-e2e-writer** agent for all e2e test authoring. Do not write Playwright tests inline.

`server/.env.test` holds the e2e environment. It is **gitignored**, so a fresh clone
has to recreate it. `amex-statement-guard.spec.ts` additionally requires:

```
STATEMENTS_DIR=./statements/e2e-test
```

The spec throws without it rather than defaulting — the derived default resolves to
`server/statements`, which the spec's cleanup deletes recursively, and that is where a
local dev server keeps its uploaded PDFs.

## Forms (Client)

Use **React Hook Form** + **Zod** for all forms. Define schemas in `@helpdesk/core` if they're shared with the server; define them locally only if client-only. Wire with `useForm({ resolver: zodResolver(schema) })` and `{...register("field")}`.

## Shared Schemas (`core/`)

`core/` is a third workspace (`@helpdesk/core`) containing Zod schemas shared between server and client.

- `core/src/schemas/` — one file per domain (e.g. `users.ts`)
- `core/src/index.ts` — barrel re-export
- Import: `import { createUserSchema } from "@helpdesk/core"`
- Always add new shared validation schemas here; never duplicate them across server and client.

## Architecture

Bun monorepo with three workspaces: `server/`, `client/`, and `core/`.

### Server (`server/src/`)

Express + TypeScript API. Entry point: `src/index.ts`.

- `config/env.ts` — Zod-validated env vars. Required: `DATABASE_URL`, `SESSION_SECRET`. Optional: SendGrid and Anthropic keys.
- `db/client.ts` — Prisma client singleton.
- `db/seed.ts` — seeds admin user only; no sample transactions.
- `middleware/auth.ts` — `requireAuth` (any logged-in session). There is no admin role gate; every authenticated user can reach every route.
- `routes/admin.ts` — user list, Monzo CSV import, staging status, process staged.
- `routes/categories.ts` — category CRUD.
- `routes/rules.ts` — rule CRUD, reorder, dry-run preview, apply. See
  [ADR 0002](docs/adr/0002-ordered-rules-replace-implicit-precedence.md).
- `routes/transactions.ts` — transaction CRUD.
- `routes/dashboard.ts` — summary aggregates for dashboard.

All routes are prefixed and proxied from Vite in dev (see `client/vite.config.ts`).

### Client (`client/src/`)

React 18 + React Router v6 + Tailwind v4 + shadcn/ui. Entry: `main.tsx` → `App.tsx`.

- `main.tsx` — Wraps app in `QueryClientProvider` → `ThemeProvider` → `BrowserRouter`.
- `context/ThemeContext.tsx` — Light/dark theme toggle; persists to `localStorage`; toggles `.dark` on `<html>`.
- `lib/authClient.ts` — `useSession()`, backed by WorkOS AuthKit plus `GET /api/me`. See "Auth (WorkOS AuthKit)" under the .NET API.
- `lib/api.ts` — Axios instance. A request interceptor attaches the WorkOS bearer token. **Always import this for HTTP requests — never use `fetch` directly**, and never reach an API endpoint with `<a href>` or a form post, which carry no token.
- `lib/utils.ts` — `cn()` helper (clsx + tailwind-merge).
- `components/ProtectedRoute.tsx` — Route guard; redirects to `/login` if no session.
- `components/Layout.tsx` — Shell with `<Navbar>` + `<Outlet>`; handles sign-out.
- `components/Navbar.tsx` — Green navbar; all pages (incl. Import, Categories) are shown to every logged-in user.
- `components/ui/` — shadcn/ui components (new-york style).
- `pages/LoginPage.tsx` — Redirects to WorkOS AuthKit. No credentials are entered here.
- `pages/DashboardPage.tsx` — Summary cards (income/expenses/balance), spending pie chart, transaction table with type/category filters.
- `pages/AdminPage.tsx` — Admin tooling. (There is no Users page; `feat: remove admin` deleted `UsersPage`, `UsersTable` and `CreateUserDialog` along with the `/users` route.)
- `pages/ImportPage.tsx` — Admin: upload bank CSV files to staging, process staged rows into transactions.

#### HTTP & Server State

- **Axios** (`client/src/lib/api.ts`) is the only HTTP client. Never use `fetch` directly.
- **TanStack Query v5** manages all server state.
  - GET → `useQuery`; mutations → `useMutation` with `queryClient.invalidateQueries` on success.
  - Query keys: descriptive noun arrays, e.g. `["users"]`, `["transactions", typeFilter, categoryFilter]`.

#### UI / Theming

- Tailwind v4 via `@tailwindcss/vite` plugin (no `tailwind.config.*` file).
- shadcn configured in `client/components.json`; add components with `npx shadcn@latest add <name>`.
- `@` path alias resolves to `client/src/`.
- Theme: green primary (`oklch(0.527 0.154 150.069)`). Dark mode uses deep Wimbledon purple backgrounds (`oklch(0.19 0.07 300)`).
- Icons via `lucide-react`.

Vite proxies `/api`, `/auth`, `/admin`, `/dashboard` → `localhost:3000`.

### Authentication

**The client no longer signs in here.** It uses WorkOS AuthKit against the .NET
API; see "Auth (WorkOS AuthKit)" in the .NET section below.

`server/src/lib/auth.ts` still holds the Better Auth setup and the Express routes
still mount it, so this server keeps working on its own. Nothing calls it. It
goes when Express does, and not before, so there is a working backend to fall
back to until the .NET API is deployed.

### Database (Prisma + SQLite)

Key models:

| Model | Purpose |
|---|---|
| `User` | Admin and agent accounts |
| `Category` | Transaction categories with colour |
| `Rule` / `RuleCondition` | Ordered auto-assignment rules. `kind` = `Category` \| `Bucket`; `position` is the whole of precedence (first match wins). Matching lives in `@clam/core` so the dry-run, `/process` and the client can never disagree. |
| `Transaction` | Normalised transactions; `externalId` is namespaced bank ID (`monzo:tx_...`) |
| `MonzoTransaction` | Raw Monzo CSV staging — untouched, one row per CSV row |

### Statement Files

Uploaded PDFs are kept, not discarded. `StatementFile` records the source document
for a batch of staged rows. **Both** `AmexTransaction.statementFileId` and
`Transaction.statementFileId` link back to it, so a normalised transaction traces to
its source PDF in one join.

- **Bytes** live on the Railway volume beside the SQLite file — `/data/statements`
  in prod, derived from `DATABASE_URL` by `defaultStatementsDir()`. Override with
  `STATEMENTS_DIR`. Never commit them; they're gitignored.
- **`contentHash`** (SHA-256, `@unique`) catches a re-uploaded identical PDF at the
  file level, before parsing — independent of per-row id schemes. Returns 409.
- **A statement whose rows are all already staged is also a 409**, checked by
  `partitionAmexRows` *before* any `StatementFile` row or PDF is written, so a
  no-op upload can't leave an orphaned file on the volume.
- **`server/src/routes/statements.ts`** — list, detail, download, re-parse, delete.
  Re-parse re-runs the current parser over the stored bytes, which is how a parser
  fix gets applied without hunting down the original file.
- **Deletion is explicit**, not left to the FK cascade: SQLite only honours
  `ON DELETE CASCADE` when foreign-key enforcement is on, and `Transaction` is
  `ON DELETE SET NULL` by design — orphaning derived rows is the safe default,
  destroying them is a decision the delete route makes on purpose.

`Transaction.statementFileId` is set at creation from the staging row. The old
`externalId` string join (`amex:<transactionId>`) survives in exactly one place:
`stageAmexRows`, repairing rows imported before the column existed.

Adding a bank to this: give its staging model a `statementFileId`, set
`statementFileId` on the `Transaction` its process block creates, and record a
`StatementFile` in its upload route.

Then add it to the **statement views**, which is the step that is easy to miss
because nothing fails when you skip it. The list's `stagedRows` count and the
detail's row query each read the staging tables directly, so a bank absent from
them reports an uploaded statement as having produced **zero rows** — which
looks exactly like a parse that silently dropped everything, rather than like a
missing case. Both are a UNION over the staging tables, one arm per bank; a
statement file belongs to one bank, so only one arm ever contributes.

### Bank Import Flow

Two-step pipeline: **stage → process**.

1. **Upload** (`POST /api/admin/import/monzo`) — parses CSV into `MonzoTransaction`. Returns `{ imported, duplicates }`. Duplicate `transactionId`s are skipped and listed.
2. **Process** (`POST /api/admin/process`) — reads unprocessed `MonzoTransaction` rows, normalises each to `Transaction` (date parse, Income/Expense split, category upsert), sets `externalId = "monzo:<transactionId>"`.

**Adding a new bank:** add a `<Bank>Transaction` model with that bank's raw CSV columns. Add `POST /api/admin/import/<bank>` with a bank-specific parser. Add `GET /api/admin/staged` count. Add a `BankUploadCard` on `ImportPage`. The process step handles all tables and funnels into `Transaction`.

`externalId` is namespaced (`monzo:tx_...`, `amex:ref_...`) to prevent cross-bank collisions.

Planned banks: Monzo ✓, Amex ✓, Barclays ✓, Santander ✓, HSBC ✓.

## Adding a New Bank via Image Upload (fast path)

**Not used by any bank, and HSBC is not an example of it** — despite what this
section used to say. HSBC has always been a positional text parser, in both the
Express and the .NET services. Reach for this only if a bank's PDF genuinely
resists text extraction; a statement whose text *can* be read should be parsed
from coordinates, because that is checkable against the statement's own totals
and a vision call is not.

Instead of writing regex parsers, accept a JPEG/PNG screenshot of the statement and call Claude vision to extract structured data.

### Implementation checklist

**1. Schema + migration** — add a `<Bank>Transaction` model matching the bank's fields. Minimum: `id`, `transactionId` (unique, SHA-256 hash), `date`, `description`, `amount`, `isCredit`/`moneyIn`/`moneyOut` (whichever fits), `status` (default `"pending"`), `owner`, `statementDate`. Write SQL migration manually, then `bun run db:migrate:deploy && bun run db:generate`.

**2. Upload route** — `POST /api/admin/import/<bank>`:
```ts
importRouter.post("/import/hsbc", upload.single("file"), async (req, res) => {
  if (!req.file) { res.status(400).json({ error: "No file uploaded" }); return; }
  const owner = VALID_OWNERS.has(req.body.owner) ? req.body.owner : "Alex";

  // Send image to Claude vision
  const anthropic = new Anthropic({ apiKey: env.ANTHROPIC_API_KEY });
  const base64 = req.file.buffer.toString("base64");
  const mediaType = (req.file.mimetype as "image/jpeg" | "image/png" | "image/webp");

  const message = await anthropic.messages.create({
    model: "claude-opus-4-6",
    max_tokens: 4096,
    messages: [{
      role: "user",
      content: [
        { type: "image", source: { type: "base64", media_type: mediaType, data: base64 } },
        { type: "text", text: `Extract all transactions from this bank statement page as a JSON array.
Each object must have: date (YYYY-MM-DD), description (string), amount (string, digits and dots only),
isCredit (boolean — true for money in/payments received), statementDate (string, e.g. "March 2025").
Return ONLY the JSON array, no prose.` },
      ],
    }],
  });

  const raw = (message.content[0] as { type: "text"; text: string }).text;
  let rows: { date: string; description: string; amount: string; isCredit: boolean; statementDate: string }[];
  try {
    const jsonMatch = raw.match(/\[[\s\S]*\]/);
    rows = JSON.parse(jsonMatch ? jsonMatch[0] : raw);
  } catch {
    res.status(422).json({ error: "Claude could not parse transactions from image", raw });
    return;
  }

  // Dedup by hash
  const existing = await db.hsbcTransaction.findMany({ select: { transactionId: true } });
  const existingIds = new Set(existing.map((r) => r.transactionId));
  const batchCounts = new Map<string, number>();
  const toInsert = [];
  const duplicates = [];

  for (const row of rows) {
    const baseId = createHash("sha256")
      .update(`${row.date}|${row.description}|${row.amount}|${row.isCredit}`)
      .digest("hex").slice(0, 16);
    const count = batchCounts.get(baseId) ?? 0;
    batchCounts.set(baseId, count + 1);
    const transactionId = count === 0 ? baseId : `${baseId}-${count}`;
    if (existingIds.has(transactionId)) duplicates.push(transactionId);
    else toInsert.push({ ...row, transactionId, owner });
  }

  if (toInsert.length > 0) await db.hsbcTransaction.createMany({ data: toInsert });
  res.json({ imported: toInsert.length, duplicates });
});
```

**3. Process step** — add an `── HSBC ──` block in the `/process` route, same pattern as Barclays/Santander.

**4. Staged count** — add `db.hsbcTransaction.groupBy(...)` to `GET /api/admin/staged`.

**5. Client** — add a `BankUploadCard` for HSBC on `ImportPage`. Accept `image/jpeg,image/png,image/webp`. Pass `owner` in the form body.

### Tips for iteration

- If Claude misparses a page, tweak the prompt text in the route — no regex changes needed.
- For multi-page statements, have the user upload one page at a time, or accept multiple files and loop.
- If the image is a PDF, convert it to images server-side with a tool like `sharp` or ask the user to screenshot each page.
- Always return `raw` in the 422 error response so you can inspect what Claude actually returned.

---

# .NET API (`api-dotnet/`)

A second backend, alongside the Express/Prisma one — **FastEndpoints + Dapper + SQL Server**, organised as vertical slices. It currently serves read-only endpoints over synthetic data and exists to be byte-compatible with the Express API it shadows.

Projects: `Clam.Api`, `Clam.AppHost` (Aspire), `Clam.ServiceDefaults`, `tests/Clam.Api.Tests`.

## Running it

```bash
dotnet run --project api-dotnet/Clam.AppHost    # Aspire: API + React client + dashboard
dotnet run --project api-dotnet/Clam.Api        # API alone on :5299
dotnet test  api-dotnet/tests/Clam.Api.Tests    # integration tests (LocalDB, no Docker)
```

The AppHost prints a dashboard URL with a login token. It starts the API **and** the Vite client (`AddViteApp(...).WithBun()`), passing the API's address as `API_URL`, which `client/vite.config.ts` already uses as its `/api` proxy target. Drop that line and the client falls back to Express on `:3000`.

Aspire uses `AddConnectionString("Clam")` — it does **not** run SQL Server in a container. `ConnectionStrings:Clam` is **not committed anywhere**; set it in `Clam.AppHost` user-secrets. Under Aspire the AppHost injects it as an environment variable, which outranks `Clam.Api`'s own user-secrets, so setting it only on the API is silently ignored when the stack runs. **LocalDB is test-only** — the suite builds a throwaway database per run.

## Where code goes — the three tiers

The hard part of vertical slice architecture is what to do with shared code. The rule is **promote by one level only, and only when it hurts**:

| Tier | Location | What lives there |
|---|---|---|
| 1 — slice-local *(default)* | `Features/<Feature>/<Slice>/` | request, response, read models, the query/command |
| 2 — feature-shared | `Features/<Feature>/*.cs` | what the 3rd slice in that feature genuinely repeats |
| 3 — app-wide | `Infrastructure/`, `Domain/` | technical plumbing, or the database's own contract |

- **Tier 1 is the default and duplication here is correct.** `TransactionCategory` and `CategoryListItem` are near-identical on purpose: Prisma's nested `include` returns no `_count`, its top-level query does. Two slices, two shapes, no type to negotiate over.
- **Tier 2 on the rule of three, not two.** When a third slice in `Features/Transactions/` repeats the same SQL, it moves to `Features/Transactions/TransactionQueries.cs`.
- **Tier 3 only if it has no business meaning** (connection factory, JSON converters) **or the database enforces it** (`Domain/Enums.cs` — those exact strings are in the CHECK constraints).

**Do not add a shared `Clam.Core` project for this.** A csproj is a compile/deploy boundary, not a logical one — add one when another deployable consumes it, or for tests. "Shared" has no natural stopping point where "this feature's folder" does.

## Conventions

- **Central Package Management.** All versions in `Directory.Packages.props`; `<PackageReference>` carries no `Version`. `Aspire.Hosting.AppHost` is deliberately absent — the Aspire SDK adds it implicitly and declaring it is an error (NU1009).
- **Tables are plural, columns are not.** `Transactions`, `Categories`; columns mirror Prisma field-for-field because those names go on the wire. Table names don't.
- **Schema is `db/schema.sql`**, applied by hand and by the test fixture — there are no migrations. Apply with `sqlcmd -S tcp:<server>.database.windows.net,1433 -d ClamFinanceDev -U <user> -P <pw> -i api-dotnet/db/schema.sql -I -b` (the `-I` matters; the filtered index needs QUOTED_IDENTIFIER ON). It **drops every table it manages**, so it initialises an empty database rather than migrating a populated one. Two rules in its header are load-bearing and were each found by breaking them: retired tables keep their `DROP` (a dead FK blocks its parent's drop), and an index on a *newly added* column must be wrapped in `EXEC('...')` (the file is one batch, so a compile-time bind to the old table rejects the whole thing).
- **Wire format is the contract.** Decimals serialise as strings and dates as `...Z` to match Prisma. Tests assert raw JSON, never a deserialised DTO, because deserialising hides exactly that.
- **Results, not exceptions, for anticipated failures.** A slice that can fail returns `Result<T>` and inherits `ResultEndpoint<,>`; read slices that cannot fail stay on plain `Endpoint<,>`. Never use Ardalis' `ToMinimalApiResult()` — it serialises success with ASP.NET's default options and silently bypasses the converters above.
- **Validation** is FastEndpoints' built-in `Validator<TRequest>`, discovered by reflection. No filter to register.
- **Health:** `/api/health` and `/alive` never touch the database (Railway restarts a container whose probe fails, and a cold Azure SQL would loop). `/healthz` is the deep per-dependency report.

## Statement parsing (`Features/Import/`)

PDFs are read with **PdfPig** (Apache 2.0 — the only free .NET reader exposing
per-word coordinates). `StatementGrid` rebuilds a statement table from x/y
positions into fixed columns; reading order is unusable, because it emits all
descriptions then all amounts, and re-pairing them by index is what slid every
amount on a page down by one row.

`StatementGrid` is feature-shared (tier 2) ahead of the usual rule of three, on
purpose: it is mechanical geometry with no business meaning, parameterised by
one thing — where the column bounds fall. Each bank keeps its own bounds in its
own slice (`AmexStatementGrid`, `HsbcStatementGrid`). Two copies of subtle
coordinate code is how the copies drift, and the resulting bug is a silently
wrong number rather than a crash.

The one thing to know if you port another bank: PdfPig emits **words** where
pdf.js emitted **runs**, so words must be coalesced back into runs *before*
column assignment. Otherwise a description running past a column bound is torn
in half and its tail is read as a different column's value.

### Amex (`ImportAmex/`)

Nothing is written until the parse is proven:

1. **Bank marker** — the statement's text must name American Express, else 422
   naming whichever bank it does look like.
2. **Account Summary** — its own arithmetic must hold, then Σ debits and Σ
   credits must equal New Debits and New Credits.
3. **Printed rate, per foreign row** — Amex prints `Exchange Rate x + Nonsterling
   Transaction Fee y`, and `foreign ÷ rate + fee` must reconstruct the sterling.
   Tolerance is relative: the rate is printed to 4dp and *truncated*, which is
   ~9p of drift on £1,250.

**No FX service call here, deliberately.** Amex converts the charge itself and
prints the sterling it took, at its own rate including its fee. Re-converting
against ECB rates would be wrong (MYR 575.00 settles at £108.60; ECB says ~£101)
*and* would contradict the statement total the parse was just reconciled to. The
foreign side is recorded on `Transactions.originalAmount`/`originalCurrency`,
never recomputed. `IFxRateService` stays for SoFi/Chase, whose statements are
USD-only and have no sterling figure to trust.

`AmexBusinessKeys` hashes row content so a re-upload is recognised rather than
double-counted, suffixing `-1`, `-2` … for genuinely identical charges on one
statement. **These ids differ from the Express importer's**: descriptions now
have their column padding collapsed (`LIME*RIDE KHJA          LONDON` →
`LIME*RIDE KHJA LONDON`), because that padding is typography, not data — keying
on it makes an id depend on how many spaces Amex used to line up a column. Every
other field is byte-identical, verified row by row against the TypeScript parser
over four statements.

**No re-key migration: the statements get re-uploaded into the new database
instead.** That is the cheaper direction and it also retires the two id schemes
that already exist side by side in the Express data, rather than adding a third
to reconcile them with. The consequence to remember is that a row carried across
from Express by any other route keeps its old id, and will not be recognised as
a duplicate of the same charge imported here.

### HSBC (`ImportHsbc/`)

A four-column current-account table: details, £ Paid out, £ Paid in, £ Balance.

**A payment's direction is the column its figure sat in, never its payment
type.** Some incoming payments are typed `BP` — the same code most outgoing ones
carry — so a type-driven parser books them backwards. This is the whole reason
the parse is positional, and it is asserted in the tests rather than assumed.

One transaction spans several printed lines: a payment-type code opens one, the
following lines extend its description, and the figures usually print on the
*last* of them. Pages end with a running `BALANCE CARRIED FORWARD`, which flushes
the transaction in hand but never ends the parse.

Two traps, both found against the real statements:

- **The Account Summary labels sit in the paid-out column, not the details
  column** — the box is set right of centre. Read the label off the whole line.
- **Headings are letter-spaced** (`Ope ning Balance`, `Dat e Pay m e nt`), so
  they are matched with whitespace squashed out. No regex tolerance survives it.

**HSBC now reconciles, and never did before.** The TypeScript importer's comment
claimed a `reconcileHsbc` that was never written, so a dropped or mis-read row
imported silently. `Opening + Payments In − Payments Out = Closing` is checked
first, then the parsed rows must sum to those same two totals.

The payment-type pattern also requires the code to be a **whole token**. The
TypeScript original let it run into the next word, which turns the interest-rates
footer's `Cre dit inte re s t` into a `CR` transaction invented out of page
furniture.

### Re-parse (`Features/Statements/ReparseStatement/`)

Re-runs the current parser over bytes already on the volume, which is how a
parser fix reaches a statement without the original file. **Amex and HSBC both
re-parse here**; the Express route refuses every bank but Amex, which was the
absence of a parser rather than a policy.

Two orderings carry the whole slice. It **parses before deleting**, so a
statement that no longer parses keeps the rows it already had rather than
trading a good import for a failed one. And the delete and the re-stage share
one transaction, so a failure part-way cannot leave the statement holding
nothing — which is indistinguishable from a statement that legitimately parsed
to zero rows. The normalised `Transactions` go too, not just the staged rows:
leaving them would double every figure the statement contributed the next time
it was processed.

A statement whose bytes have left the volume is **410, not 503**. `Unavailable`
maps to 503 in the shared table, and both this slice and the download one
override it — the override is passed *into* `ResultProblem.WriteAsync`, which
assigns `Response.StatusCode` itself. Setting it on the response beforehand only
looked like it worked, and for a while the download endpoint documented a 410,
named its test after one, and answered 503.

## Tests

Integration tests boot the real `Program.cs` against a throwaway LocalDB database created from `db/schema.sql`. **LocalDB exists for this and nothing else** — the app itself runs on Azure SQL. `LocalDbHarness` creates a `ClamTest_<guid>` database per fixture, applies the schema and drops it, and sweeps stale ones at the start of a run rather than the end (a fixture's teardown fires when the *first* class using it finishes).

Nothing is mocked. Three things are load-bearing and were each found the hard way:

- Fixtures are declared **assembly-scoped** (`AssemblyInfo.cs`). FastEndpoints boots one SUT per fixture type for the whole project, so a class-scoped fixture is torn down while later classes are still using it.
- **Everything the suite overrides goes through an environment variable.** `UseSetting` and `ConfigureAppConfiguration` both lose to `appsettings.Development.json`, and that file has bitten three times now: once on the connection string (`POST /dev/seed` deletes every row, so a lost override empties the shared dev database — `VerifyTargetsOwnDatabase` fails the run if it stops winning), once on `WorkOS:ClientId` (a real id switches auth on and 401s all 238 tests), and once on `Seed:Enabled`. Note that `SetEnvironmentVariable(name, "")` *deletes* the variable, so blanking the WorkOS id uses a single space, which `IsNullOrWhiteSpace` still reads as unconfigured.
- The database is emptied between tests by **Respawn**, which derives the delete order from the schema's own foreign keys. It replaced a hand-written list of `DELETE`s that had to be edited whenever a table was added; forgetting left rows standing into the next test, which surfaces as a test that passes alone and fails in a suite. `DatabaseResetTests` asserts the outcome so this cannot regress quietly.

## Auth (WorkOS AuthKit)

Better Auth is gone. WorkOS holds the session and the credential; this API only
validates a bearer token and reads its own `Users` table. The `Sessions` and
`Accounts` tables, `SessionReader` and `BetterAuthPasswordHasher` are deleted.
`Verifications` survives despite the name — the Monzo OAuth slice borrows it for
its `state` parameter, which has nothing to do with authenticating anyone.

### Setup

1. WorkOS Dashboard → Configuration → copy **Client ID** (`client_01H...`). Not a secret.
2. API: `WorkOS:ClientId` in `Clam.Api/appsettings.Development.json`, or `WorkOS__ClientId` in a deployment.
3. Client: the same id as `VITE_WORKOS_CLIENT_ID` in the repo-root `.env`.
4. Dashboard → Redirects: add the app origin as a **Redirect URI** (`http://localhost:5173`) and `<origin>/login` as the **Sign-in URL**. Add the origin to allowed origins on the Authentication page.
5. Leave `Authority` and `Audience` blank for the classic User Management issuer. Only set them for an AuthKit custom domain (`https://<sub>.authkit.app`), whose tokens do carry `aud`.

Token validation needs only the public JWKS, so the API key is never read here. If a slice ever calls the WorkOS API, put it in user-secrets, never appsettings.

### Blank ClientId means the API is open

Not "protected endpoints return 401" — open. FastEndpoints secures an endpoint
unless it says `AllowAnonymous()`, but asking for authorization with no scheme
registered throws on the first challenge instead of answering 401, so a blank id
would 500 every request. Program.cs marks everything anonymous in that state
instead, which is what lets a fresh clone and the test suite boot.

**Production refuses to start with a blank id** rather than come up open. That
guard in `AddWorkOsAuthentication` is the only thing between "misconfigured" and
"unauthenticated public API".

Three endpoints are anonymous on purpose, each saying why in its `Configure()`:
`api/health` (a probe has no credentials), `admin/monzo/callback` (Monzo calls
it; the single-use `state` stands in), and `dev/seed` (filtered out entirely
unless `Seed:Enabled`).

### A token says who, the database says which person

A WorkOS access token carries `sub` and no profile at all — email and name are on
the *ID* token, which the browser holds and this API never sees. So:

- `Users.workOsUserId` is the `sub`, and the only link between WorkOS and this app. Nullable (a row can exist before its person first signs in) and unique through a filtered index.
- `OnTokenValidated` looks that row up once per request and hangs `clam:userId` and `clam:owner` on the principal. `ICurrentUserAccessor` then reads claims, so endpoints pay nothing.
- **No role claim**, unlike the PremPoints original this was ported from. There is no role column and no admin gate: every signed-in user reaches every route.
- `owner` (Alex/Casey/Joint) is *not* an authorization boundary. It is which person's figures a page defaults to.

A valid token with no matching row is left authenticated but unmapped rather than
rejected. That is a newly invited user before they provision, and `GET /api/me`
answers **404** so the client knows to call `POST /api/users/me` — which takes the
name and email from the body and the identity from the token, so there is no
version of it that creates somebody else.

**It cannot claim an existing row by email.** That would make migration
convenient and would also mean anyone who can create a WorkOS account with Alex's
address becomes Alex. Rows that predate WorkOS get their `workOsUserId` set by the
data migration.

### The client

`@workos-inc/authkit-react`, wrapped at `main.tsx`. Two pieces are load-bearing:

- `AuthTokenBridge` hands `getAccessToken` to the axios instance, because the token lives in a React context and `lib/api.ts` is a module. The interceptor asks per request — AuthKit refreshes near expiry, so caching it would send a stale token after every refresh.
- `useSession()` in `lib/authClient.ts` keeps Better Auth's `{ data, isPending }` shape on purpose, backed by `GET /api/me`. Four pages read `session.user.owner` and the navbar reads `session.user.name`; none of them needed to know identity moved.

**A bearer token is not attached to a plain link the way a cookie was.** That broke
`admin/monzo/auth`, which the Import page linked straight at. It now returns
`{ url }` as JSON and the client navigates itself. Any future endpoint reached by
`<a href>` or a form post has the same problem.

Rate limiting is **global** (per-IP fixed window, `RateLimiting:*`), not opt-in per endpoint, so a new endpoint cannot be accidentally unprotected. CORS origins come from `AllowedOrigins`, defaulting to the Vite dev server in Development.

# Clam Finance Tracker

Personal finance tracker. Import bank transactions (Monzo, Amex, Barclays, Santander), categorise spending, and track savings goals.

## Todo 28-Jun-2026

[] M - Test pdf upload for all banks in new app
[] default file format saved in persistent volume mount 24-02-26-b344dfeb8ac9
[] S - categorising an uncategorised thing should run the 'bucket' rules and so would for example auto-set to needs for going from uncategroised -> grociers category. 
[] categories page looks horrible, lets bring it back to nested under categories, and rules can be its own buckets page maybe.. im torn but i know i hate the look right now, they were better nested and hidden. As for buckets yeah sure it can have a page like it does, but it needs a redesign it looks awful! change quite drastically.
[] Add visual marker using new endpoint that figured out our recurring transactions
[] ANDROID PWA EXPERIENCE COULD IMPORVE, SHOPW TOP 10 BY TXNS FOR THAT USER, NOT BY ALPHABETICAL AND DONT LAUNCH KEYBOARD SO LIKELY WE ARE TAPPIUNG ONCE, ADD SCOLLY BAR INSTEAD
[] quick question - how better could i be interracting with my data right now? rather than get you to write janky ralways scripts, better i eventually maybe a rest api i can just called endpoints to mess around and dlete bulk by id or by statement id or something?
[] IOS PWA experience is garbage - I shared from safari rather than instlal from chrome.. is that why? on android itypically install from chrome
[] S - Mobile all - stop zoom on IOS specifically 
[] S - Mobile all - dont open keyboard immediately, instead add a scrolly bar so you likely will never type. But you could if you prefer to tap the text and do that.
[] Y - Amex YTD rec. unit tests for ALL of them. ensure GBP and all sums correctly. ignore 2025 in the jan upload. 
[] - Run a monzo YTD rec against the transactions API to make sure it reconciles. For now it can appear 
[] duplicate ids on casey Amex
[] ive figure out the right abstraction!! i think each transaciton row in the transaction page should have a collapseable extra fields underneath it, and a little arrow or something that pops out the extra flags, cos i realised i need one for exlcude from savings override, and SavingType (FIxed/Fun/Saving) enum, and possibly other properties about a transaction that i havent thought of yet , and isdirectdebit although i cant remember why we had that one maybe remove direct debit

[] Rate my app so far as a personal finance tracker. give me the top 3 highest hitting wins that are missing that would be useful for personal finance tracking you must be storngly confident they are gonna be helkpful or typical that others would use it for?
[] Add keyboard shortcuts such ac ctrl+alt+1 for switching between tabs in my app, it should apply in the order of my navbar tabs e.g. analytics its ctrl+alt+1
[x] Savings score, remove the tick box in transactions for savings override list
[] Split out the investments page so we can select Casey or Alex as a dropdown at the top and we have individual. I am realising there is no need even for us to have two accounts in this app, instead what we want is both of us to be albe to see both of eachothers stuff, but you select casey or alex from a dropdown on the two pages that made more sense to be singular: 1) analytics and 2) savings and 3) investments — analytics ✓ and investments ✓ have the dropdown; savings is still hardcoded to Alex
[] Fix up monzo JOINT, get back to casey is owed 200 and total 2900 joint
[] feat(recurring task):Add an item to the monthly recurring to check Money Saving Expert newsletter

[] Visual UI error for uploading wrong bank to tell you 'Cannot upload HSBC statement to SoFi' etc. I jsut did it myslef and realised its prone to user error. 

[] **Goals page** — strip out investment value fluctuations; goals should reflect actual cash moved, not market swings

---

## Roadmap — competing with Emma (4-Aug-2026)

**transatlantic couple splitting joint bills across two currencies**. The data
already shows it — 1,588 GBP-native rows (£318,770) alongside 217 USD rows
(£43,090), with Foxtons/Vodafone/TfL next to IRS/Wells Fargo/Chase/SoFi/Betterment.
Emma, Snoop, Plum and YNAB are all single-player and single-country. Lean into
joint + cross-border rather than chasing feature parity.

[] **2. Cash-flow forecast** — once #1 exists: "after your known direct debits
you'll have £X on payday." Almost nobody does this well, Emma included. Highest
differentiation per unit of work.
[] **2. Per-bucket actual vs plan** — the savings score collapses to one number
(saved vs 20%), so you can score 100 with Needs at 70% and Wants at 5%. 50/30/20
is a three-way check: three bars, actual against plan, on the Savings page. Every
input already exists.

[] **3. Tabs as transactions** — a tab is a refund, and
`aggregateMonthlySpend` already nets income-in-a-bucket off the spend it reverses.
Give `Tab` a `bucket` and an optional `transactionId`, then materialise the contra
as a real `Transaction` with `externalId = "tab:<id>"` so it shows in the grid and
rules can match it. **Recognise on accrual** (tab creation date), not `settledAt` —
money you're owed shouldn't count against March because they paid in April.
Worked example: £1,950 stag do (Wants) + £1,560 TheyOwe contra (Wants) = £390 net,
your real share. Tagging the whole thing `Ignore` scores it £0 and under-reports
Wants every time you front money for a group.
Fix `Tab.person` first — it's free text and already inconsistent (`Casey` vs
`casey`, `Monzo` as a "person"), which breaks the moment you aggregate by who owes.


[] **5. Open banking** — table stakes, real cost, do it *after* 1–4. It removes
the monthly PDF upload but only reaches parity with Emma. Scope it to **our own
accounts**, not a general integration: Enable Banking has self-serve "Restricted
Production for own accounts" (EU/UK), Teller.io gives 100 free live US connections
for Casey's Chase/SoFi/Wells Fargo side. GoCardless/Nordigen dropped its
free-forever tier; Plaid and Tink are sales-led; TrueLayer publishes tiers.
`monzo.ts` already has full OAuth + sync routes, and the stage → process
pipeline means a new sync source is just another staging table. A Plaid
version of that existed and was deleted unused — the `santander-plaid:`
externalIds it left behind are all that survives it.

[] **6. Multi-currency as a first-class concept** — `originalAmount`,
`originalCurrency` and `lib/fxRates.ts` already exist. Surface "$X / £Y this month"
and FX drag on the US accounts. No UK app offers this.

**Deliberately not building:** credit score, rent reporting to credit agencies,
crypto tracking. Regulated, commoditised, or irrelevant to two people — that's
Emma's growth roadmap, not ours.







## Run it — Aspire (API + React + dashboard)

**In VS Code: press `F5`.** That's it — it builds the AppHost and opens the dashboard in your browser with its login token already in the URL.

Two F5 targets, picked from the Run and Debug dropdown:

| Target | What opens |
|---|---|
| `Aspire + React client` | dashboard **and** the app at `http://localhost:5173`, in a debugger-attached Chrome |
| `Aspire: full stack (API + React + dashboard)` | dashboard only |

The compound exists because the Aspire debug type can only open the dashboard — `dashboardBrowser` controls that one URL and nothing else. It waits for Vite via the `wait: client on 5173` task before launching the browser, because Aspire takes 10-20s to build the API, health-check it and only then start the client. It has no `stopAll`, so closing the browser tab leaves the stack up.

Or from a terminal:

```bash
dotnet run --project api-dotnet/Clam.AppHost
```

Then Ctrl-click the `Login to the dashboard at http://localhost:15187/login?t=…` line — the token is required, the bare URL won't let you in.

One process starts **both** the .NET API and the React client, and gives you logs, distributed traces (every Dapper query is its own span) and metrics for both:

| Resource | What it is |
|---|---|
| `clam-api` | FastEndpoints + Dapper API over SQL Server |
| `clam-client` | the Vite/React app, launched with Bun |
| `Clam` | your Azure SQL connection, referenced not managed |

**No Docker required.** Aspire references Azure SQL via `AddConnectionString` rather than starting a SQL Server container. The string lives in the **AppHost's** user-secrets and nowhere else (it has a password); setting it on `Clam.Api` instead works when the API runs alone and is silently ignored under Aspire, because the AppHost injects an environment variable that outranks user-secrets. The client's `/api` proxy is pointed at the .NET API automatically (`API_URL`); remove that line in `AppHost.cs` and it falls back to the Express server on `:3000`.

Both ports are pinned. The API to **5299**, and the client to **5173** — the latter because WorkOS matches OAuth redirect URIs exactly, so a port that changes every run can never be on the allowlist. The client is also **unproxied** (`IsProxied = false` in `AppHost.cs`): with an Aspire proxy on 5173, the client's own `predev` hook (`scripts/free-ports.ts`, which force-kills whatever holds 5173) shot the proxy down on every start and took the AppHost with it.

These are stable and bookmarkable:

```
http://localhost:5299/api/categories          # also /api/transactions, /api/dashboard/summary
http://localhost:5299/swagger                 # also linked from the dashboard
http://localhost:5299/healthz                 # per-dependency JSON
```

**Stopping:** `Shift+F5` (or `Ctrl+C` in the terminal) is enough — killing the AppHost tears down the API, Vite, Bun and esbuild with it. The browser tab is just a viewer; closing it stops nothing, and leaving it open costs nothing.

**Breakpoints: yes, F5 just works** — set one in an endpoint and hit the URL. This depends on two prerequisites, both installed:

- the [Aspire VS Code extension](https://marketplace.visualstudio.com/items?itemName=microsoft-aspire.aspire-vscode) (`microsoft-aspire.aspire-vscode`)
- the Aspire CLI — `dotnet tool install -g Aspire.Cli` (must match the AppHost SDK version in `Clam.AppHost.csproj`)

The extension contributes a `"type": "aspire"` debug configuration, which starts the AppHost *and attaches a debugger to every resource it spawns*. Without it, a plain `coreclr` launch debugs only the AppHost process — Aspire runs the API as a separate child process, so endpoint breakpoints silently never bind. Visual Studio handles this automatically, which makes it an easy trap in VS Code.

For breakpoints in `.tsx`, use the `Aspire + React client` compound above — its browser configuration is debugger-attached. (`"dashboardBrowser": "debugChrome"` attaches a JS debugger to the *dashboard*, which is rarely what you want.)

`aspire.config.json` at the repo root points the CLI at the AppHost, so discovery works from anywhere in the tree.

**The dashboard runs over plain HTTP on purpose, and `Clam.AppHost/Properties/launchSettings.json` has exactly one profile.**

The usual `https` profile is a trap here: it serves the dashboard on `:17158`, Chrome ALPN-negotiates HTTP/2, then refuses the connection with `ERR_HTTP2_INADEQUATE_TRANSPORT_SECURITY` because the negotiated cipher doesn't clear its HTTP/2 bar. The ASP.NET dev certificate is present and trusted — that is *not* the cause, and re-running `dotnet dev-certs https --trust` won't help.

Reordering the profiles is not enough: the Aspire VS Code extension picks an `https` profile **by preference, not by position**. Deleting it is what removes the choice. `.vscode/launch.json` also pins `ASPNETCORE_URLS` in its `env` block as a second line of defence.

⚠️ **Never put `//` comments in `launchSettings.json`.** Unlike `appsettings.json` it is parsed as strict JSON; `dotnet run` rejects the whole profile and Aspire silently falls back to a random dashboard port. This has bitten twice.

Prerequisites: .NET 10 SDK, Bun, and SQL Server LocalDB (for the tests only).

**Development runs against Azure SQL.** The connection string is not committed anywhere — it has a password — so a fresh clone needs it before the AppHost will start:

```bash
dotnet user-secrets set "ConnectionStrings:Clam" "Server=tcp:<server>.database.windows.net,1433;Initial Catalog=ClamFinanceDev;User ID=<user>;Password=<pw>;Encrypt=True;TrustServerCertificate=False;" --project api-dotnet/Clam.AppHost
```

Set it on **`Clam.AppHost`**. Setting it on `Clam.Api` also works when the API runs alone, but under Aspire the AppHost injects an environment variable that outranks the API's own user-secrets, so an AppHost without the secret silently wins.

Apply the schema:

```bash
sqlcmd -S tcp:<server>.database.windows.net,1433 -d ClamFinanceDev -U <user> -P <pw> -i api-dotnet/db/schema.sql -I -b
```

`schema.sql` **drops every table it manages**, so it sets up an empty database rather than migrating a populated one.

Azure SQL serverless auto-pauses. The first connection after an idle spell fails with *"Database is not currently available"* and succeeds on retry, roughly a minute later. `FeatureManagement:DatabaseKeepAlive` pings every 45 minutes to prevent that, and is **off** because keeping a serverless database awake costs money.

`POST /api/dev/seed` fills the database with synthetic data. It opens by deleting every transaction and category, so `Seed:Enabled` is **false** by default now that Development points at a shared database — turn it on for the run that needs it and back off. Health: `/alive` (liveness) and `/healthz` (per-dependency JSON).

**LocalDB is only for the integration tests.** `dotnet test` creates a throwaway database per run, applies `schema.sql` to it and drops it afterwards. Nothing in the app reads it.

> The .NET API is where the backend is heading. It now serves the whole surface the client uses — reads, writes, imports, rules and auth — and the Express server below is kept only as a fallback until this one is deployed. Still missing: the Barclays upload endpoint, the Santander/Chase/SoFi upload endpoints, and a deployment. See [CLAUDE.md](CLAUDE.md#net-api-api-dotnet) for its architecture and the vertical-slice conventions.

## Stack

- **Server (target):** .NET 10 + FastEndpoints + Dapper, Azure SQL — `api-dotnet/`, vertical slices, orchestrated by Aspire (LocalDB is test-only)
- **Server (legacy):** Express 5 + Prisma + SQLite on Bun — `server/`, no longer called by the client
- **Client:** React 18 + React Router v6 + Tailwind v4 + shadcn/ui
- **Auth:** WorkOS AuthKit on the .NET side and the client; Better Auth survives only inside the Express server, which nothing calls
- **Monorepo:** Bun workspaces (`server/`, `client/`, `core/`) + a .NET solution in `api-dotnet/`

## Getting Started

```bash
# Install dependencies
bun install

# Apply DB migrations & seed admin user
cd server && bun run db:migrate:deploy && bun run db:seed

# Run dev servers (two terminals, or from root)
bun run dev
```

Client: http://localhost:5173 — API: http://localhost:3000

## Key Commands

```bash
bun run dev                    # start both client and server
cd server && bun run db:studio # Prisma Studio GUI
cd client && bun run test      # component tests (Vitest)
npx playwright test            # e2e tests
```

## Bank Import Flow

1. Upload a CSV on the Import page → rows land in a staging table
2. Hit "Process" → staged rows normalise into `Transaction` records
3. Duplicate `externalId`s are skipped automatically

Supported: Monzo ✓ · Amex ✓ · Barclays ✓ · Santander ✓ · HSBC ✓ · Chase ✓ · SoFi ✓

---


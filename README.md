# Clam Finance Tracker

Personal finance tracker. Import bank transactions (Monzo, Amex, Barclays, Santander), categorise spending, and track savings goals.

## Stack

- **Server:** Express 5 + Prisma + SQLite (Bun runtime)
- **Client:** React 18 + React Router v6 + Tailwind v4 + shadcn/ui
- **Auth:** Better Auth (server-side sessions)
- **Monorepo:** Bun workspaces (`server/`, `client/`, `core/`)

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

## Todo 28-Jun-2026

[] categories page looks horrible, lets bring it back to nested under categories, and rules can be its own buckets page maybe.. im torn but i know i hate the look right now, they were better nested and hidden. As for buckets yeah sure it can have a page like it does, but it needs a redesign it looks awful! change quite drastically.
[] Add visual marker using new endpoint that figured out our recurring transactions
[] ANDROID PWA EXPERIENCE COULD IMPORVE, SHOPW TOP 10 BY TXNS FOR THAT USER, NOT BY ALPHABETICAL AND DONT LAUNCH KEYBOARD SO LIKELY WE ARE TAPPIUNG ONCE, ADD SCOLLY BAR INSTEAD
[] quick question - how better could i be interracting with my data right now? rather than get you to write janky ralways scripts, better i eventually maybe a rest api i can just called endpoints to mess around and dlete bulk by id or by statement id or something?
[] IOS PWA experience is garbage - I shared from safari rather than instlal fgrom chrome.. is that why? on android itypically install from chrome
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


[X] **1. Recurring / subscription detection** — Emma's flagship feature, and the
data makes it nearly free. `group by description having count(distinct month) >= 4`
already surfaces `GOOGLE*YOUTUBE £12.99 × 6`, `SO ENERGY £67.69 × 6`,
`CONNECTIVITY £60.24 × 6`. No external dependency, no regulatory exposure, and it
unlocks #2. Build this first.
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
`plaid.ts` and `monzo.ts` already have full OAuth + sync routes, and the
stage → process pipeline means a new sync source is just another staging table.

[] **6. Multi-currency as a first-class concept** — `originalAmount`,
`originalCurrency` and `lib/fxRates.ts` already exist. Surface "$X / £Y this month"
and FX drag on the US accounts. No UK app offers this.

**Deliberately not building:** credit score, rent reporting to credit agencies,
crypto tracking. Regulated, commoditised, or irrelevant to two people — that's
Emma's growth roadmap, not ours.

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

Supported: Monzo ✓ — Amex, Barclays, Santander, Caseys banks coming soon. 

---

## Todo 28-Jun-2026
[] duplicate ids on casey Amex
[] check why hsbc latest date says 1st april, not 8th april, for casey apr statement
[] why does the savings page still have this stuff for the exclusion items? [] ive figure out the right abstraction!! i think each transaciton row in the transaction page should have a collapseable extra fields underneath it, and a little arrow or something that pops out the extra flags, cos i realised i need one for exlcude from savings override, and SavingType (FIxed/Fun/Saving) enum, and possibly other properties about a transaction that i havent thought of yet , and isdirectdebit although i cant remember why we had that one maybe remove direct debit

[] Rate my app so far as a personal finance tracker. give me the top 3 highest hitting wins that are missing that would be useful for personal finance tracking you must be storngly confident they are gonna be helkpful or typical that others would use it for?
[] Categories should also have default mappings per bank we can enter in a nice looking table in the categories page, it should use a waterfall style table so i can apply to either all banks, or just alex oir jsut casey, or just one bank for example, tesco all banks --> groceries, amex alex lime --> transport, casey amex lime --> fixed costs something like that, and use the wildcard * to mean applies to all in this large table format! if that isnt how categoroies already work in this app, tell me if the landscape is more complicated than that, and if you feel it would bne realistic to make it work my way instead!!
[] Add keyboard shortcuts such ac ctrl+alt+1 for switching between tabs in my app, it should apply in the order of my navbar tabs e.g. analytics its ctrl+alt+1
[] Savings score, remove the tick box in transactions for savings override list
[] Split out the investments page so we can select Casey or Alex as a dropdown at the top and we have individual. I am realising there is no need even for us to have two accounts in this app, instead what we want is both of us to be albe to see both of eachothers stuff, but you select casey or alex from a dropdown on the two pages that made more sense to be singular: 1) analytics and 2) savings and 3) investments
[] Fix up monzo JOINT, get back to casey is owed 200 and total 2900 joint
[] feat(recurring task):Add an item to the monthly recurring to check Money Saving Expert newsletter

[] Visual UI error for uploading wrong bank to tell you 'Cannot upload HSBC statement to SoFi' etc. I jsut did it myslef and realised its prone to user error. 

[] **Goals page** — strip out investment value fluctuations; goals should reflect actual cash moved, not market swings

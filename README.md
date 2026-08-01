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

[] for category rules, make them act more like ag-grid string filters, so i have more control i.e. allow me a dropdown to choose contains/ starts with/endswith and exact match! and still have the run rules, and allow a dryrun option on run rules before we confirm it so i can see whats gonna be affected before we smash it in !!
[] stats page - monthyl count of transacitons
[] keep the rows but automatically load with a ag-grid filter of 'not zeros' for this month so we can see less rows to have to enter! it should be removable
[] Auto-bucketing for neeeds and wants should also be customisable similar to how category auto-rules are.Decide if you think best to put in the categories page, or have another new page. Might be best to have one called rules that emcompasses both cat and bucketing maybe.. you should be able to make rules that can account for combinations of transaction descriptions, and categories, to decide a thing e.g. transport defaults to needs, transport + uber goes to wants, etc. then salary defaults to be ignored since its not part of your spending !
[] ANDROID PWA EXPERIENCE COULD IMPORVE, SHOPW TOP 10 BY TXNS FOR THAT USER, NOT BY ALPHABETICAL AND DONT LAUNCH KEYBOARD SO LIKELY WE ARE TAPPIUNG ONCE, ADD SCOLLY BAR INSTEAD
[] quick question - how better could i be interracting with my data right now? rather than get you to write janky ralways scripts, better i eventually maybe a rest api i can just called endpoints to mess around and dlete bulk by id or by statement id or something?
[] IOS PWA experience is garbage - I shared from safari rather than instlal fgrom chrome.. is that why? on android itypically install from chrome
[] S - Mobile all - stop zoom on IOS specifically 
[] S - Mobile all - dont open keyboard immediately, instead add a scrolly bar so you likely will never type. But you could if you prefer to tap the text and do that.
[] Y - Amex YTD rec. unit tests for ALL of them. ensure GBP and all sums correctly. ignore 2025 in the jan upload. 
[] Y - Amex YTD rec. unit tests for ALL of them. ensure GBP and all sums correctly. ignore 2025 in the jan upload. 
[] - Run a monzo YTD rec against the transactions API to make sure it reconciles. For now it can appear 
[] Needs/Wants/Savings/Ignore should be the 4 categories, reove inherit concept. instad, you should be able to set up your own mappings similar to the cateogries where i can know that for example, investments category goes to savings, and my rent goes to needs etc. so i dont need to do much work re-categorising, but equally if its unclear which it should be, we should leave uncategorised I think. Default is unset, but it wont show that in the UI as that another option, once youve chosen one you cant go back to null, simply can flip between them
[] duplicate ids on casey Amex
[] why does the savings page still have this stuff for the exclusion items? [] ive figure out the right abstraction!! i think each transaciton row in the transaction page should have a collapseable extra fields underneath it, and a little arrow or something that pops out the extra flags, cos i realised i need one for exlcude from savings override, and SavingType (FIxed/Fun/Saving) enum, and possibly other properties about a transaction that i havent thought of yet , and isdirectdebit although i cant remember why we had that one maybe remove direct debit

[] Rate my app so far as a personal finance tracker. give me the top 3 highest hitting wins that are missing that would be useful for personal finance tracking you must be storngly confident they are gonna be helkpful or typical that others would use it for?
[] Add keyboard shortcuts such ac ctrl+alt+1 for switching between tabs in my app, it should apply in the order of my navbar tabs e.g. analytics its ctrl+alt+1
[] Savings score, remove the tick box in transactions for savings override list
[] Split out the investments page so we can select Casey or Alex as a dropdown at the top and we have individual. I am realising there is no need even for us to have two accounts in this app, instead what we want is both of us to be albe to see both of eachothers stuff, but you select casey or alex from a dropdown on the two pages that made more sense to be singular: 1) analytics and 2) savings and 3) investments
[] Fix up monzo JOINT, get back to casey is owed 200 and total 2900 joint
[] feat(recurring task):Add an item to the monthly recurring to check Money Saving Expert newsletter

[] Visual UI error for uploading wrong bank to tell you 'Cannot upload HSBC statement to SoFi' etc. I jsut did it myslef and realised its prone to user error. 

[] **Goals page** — strip out investment value fluctuations; goals should reflect actual cash moved, not market swings

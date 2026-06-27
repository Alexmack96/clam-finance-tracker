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

## Todo 27-Jun-2026
[] run prod dedup script for barclays
[] Fix up monzo JOINT, get back to casey is owed 200 and total 2900 joint
[] Monzo OAUTH

    1. Register a proper OAuth client on developers.monzo.com

    Go to Clients → Create a new client
    Set redirect URI to http://localhost:3000/api/admin/monzo/callback
    Copy the client_id and client_secret
    2. Fill in server/.env


    MONZO_CLIENT_ID=oauth2client_xxx
    MONZO_CLIENT_SECRET=your_secret
    MONZO_REDIRECT_URI=http://localhost:3000/api/admin/monzo/callback

[] feat(recurring task):Add an item to the monthly recurring to check Money Saving Expert newsletter

[] Visual UI error for uploading wrong bank to tell you 'Cannot upload HSBC statement to SoFi' etc. I jsut did it myslef and realised its prone to user error. 


[] **Goals page** — strip out investment value fluctuations; goals should reflect actual cash moved, not market swings
[] **"In my control" tagging** — mark transactions as outside my control (e.g. a £100 BA charge I don't regret because I made the right call at the time); exclude or surface separately so they don't distort how I feel about spending


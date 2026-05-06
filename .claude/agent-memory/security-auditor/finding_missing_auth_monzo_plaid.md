---
name: Missing top-level auth on monzo/plaid routers
description: monzoRouter and plaidRouter are mounted at /api/admin/monzo and /api/admin/plaid without requireAuth/requireAdmin at mount site; auth is only on individual endpoints, creating gap risk
type: project
---

monzoRouter mounted at line 74, plaidRouter at line 75 of server/src/index.ts — neither has requireAuth/requireAdmin at the mount point unlike all other /api/admin routes. Individual endpoints add guards inconsistently: /callback has NO auth at all.

**Why:** These routes were added after the pattern was established for other /api/admin routes.

**How to apply:** Flag any new route added to /api/admin/monzo or /api/admin/plaid that lacks explicit requireAuth as a potential auth gap. Recommend moving guards to mount site.

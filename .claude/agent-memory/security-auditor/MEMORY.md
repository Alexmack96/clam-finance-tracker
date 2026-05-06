# Security Auditor Memory Index

- [Missing auth on monzo/plaid routes](finding_missing_auth_monzo_plaid.md) — monzoRouter and plaidRouter mounted without top-level requireAuth/requireAdmin; guards only on individual endpoints
- [Multer no file size or type limits](finding_multer_no_limits.md) — all upload routes use bare multer({ storage: memoryStorage() }) with no fileSize or fileFilter
- [Raw PDF text leaked in error responses](finding_raw_leak.md) — Chase 422 route returns raw: text.split("\n") — full PDF text in error body
- [sendDefaultPii true in Sentry](finding_sentry_pii.md) — Sentry initialized with sendDefaultPii: true; sends cookies, IP, user data to third party
- [ADMIN_PASSWORD in env schema](finding_admin_password_env.md) — ADMIN_PASSWORD loaded at runtime and validated by Zod but not used at runtime; risk is env var exposure surface
- [No upload rate limiting](finding_no_upload_ratelimit.md) — rate limiter only on /auth sign-in; all file upload and process endpoints have no rate limit
- [Monzo callback unauthenticated](finding_monzo_callback_unauth.md) — /api/admin/monzo/callback has no requireAuth; state verification is present but no session check
- [Dashboard full table scan](finding_dashboard_full_scan.md) — /api/dashboard/summary fetches all transactions with no pagination — DoS risk on large DB
- [Transactions type cast without enum validation](finding_transactions_type_cast.md) — query params cast to TransactionType/Owner without Zod validation; relies on Prisma rejecting bad values
- [Chase raw PDF in 422 response](finding_raw_leak.md) — same file as raw leak finding above

---
name: Raw PDF text leaked in error responses
description: Chase import 422 and parse-error responses return raw: text.split("\n") — the full extracted PDF text, potentially including PII, in the API response body
type: project
---

server/src/routes/import.ts lines 886 and 891. The 422 path returns the entire PDF text content as an array.

**Why:** Added for debug convenience during development; not safe in production.

**How to apply:** Never return raw file content in error responses. Return only: { error: "...", rowsFound: N } or similar. Remove the raw field before or behind a NODE_ENV=development guard.

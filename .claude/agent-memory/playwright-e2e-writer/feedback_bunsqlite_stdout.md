---
name: feedback-bunsqlite-stdout
description: How to reliably read bun:sqlite query results from runBunScript on Windows — avoid console.log and column alias 'n'
type: feedback
---

In the `runBunScript` helper (identical pattern across e2e spec files), two issues cause NaN when reading numeric query results on Windows:

1. **Column alias `n` comes back as `undefined`** under some Playwright worker conditions. Use a different alias (e.g. `cnt`) to avoid the collision. `console.log(r.n)` prints `"undefined"` → `parseInt` → NaN.

2. **Use `process.stdout.write(String(value) + '\\n')` not `console.log(value)`** to print the result. This ensures the exact value reaches stdout without any potential buffering or logging-layer interference.

3. **Specify `stdio: ['pipe', 'pipe', 'pipe']` explicitly on `execSync`** and wrap in try/catch to surface bun script errors as readable Playwright test failures rather than silent empty output.

**Why:** Discovered while writing `e2e/barclays-dedup.spec.ts` — `console.log(r.n)` returned NaN consistently in Playwright workers even though the same script worked fine when run directly. Changing to `cnt` alias + `process.stdout.write` fixed it.

**How to apply:** Whenever writing a new `countXxx()` or query helper using `runBunScript`, always use a non-`n` alias and `process.stdout.write`.

/**
 * Transactions page (/transactions, DashboardPage.tsx) rendering-performance
 * regression coverage.
 *
 * Background: with ~1.8k transactions the page took ~5s to become interactive
 * because of two bugs, both now fixed:
 *
 *   1. AG Grid used `domLayout="autoHeight"`, which (per AG Grid docs) renders
 *      every row into the DOM instead of virtualising. Fixed by removing
 *      autoHeight and giving the grid a fixed-height container (70vh), which
 *      re-enables row virtualisation.
 *   2. The mobile card list and the desktop AG Grid were both toggled with
 *      CSS only (`md:hidden` / `hidden md:block`), so both trees mounted in
 *      React simultaneously regardless of viewport. Fixed by gating each tree
 *      on `useIsDesktop()` (a real `matchMedia` check) so only one mounts.
 *
 * Seeding: global-setup only seeds the admin user (zero transactions/
 * categories), so this spec seeds ~1500 Transaction rows + 1 Category
 * directly against the test SQLite DB via bun:sqlite (same pattern as
 * monzo-sync.spec.ts / barclays-dedup.spec.ts), and cleans up afterAll.
 *
 * Tests:
 *   1. Desktop viewport: AG Grid renders far fewer than 1500 `.ag-row`
 *      elements (virtualisation on) while the page's own header still
 *      reports the full 1500 total. Mobile card list markup is absent from
 *      the DOM (not just hidden).
 *   2. Mobile viewport (390x844): mobile card list is present; AG Grid root
 *      is absent from the DOM (not just hidden).
 *
 * Both assertions use `toHaveCount(0)` for absence (not visibility checks) —
 * CSS-only hiding is exactly the regression being locked in, so a visibility
 * assertion would pass against the old broken code.
 */

import { test, expect } from "./fixtures.js";
import { execSync } from "child_process";
import { readFileSync, writeFileSync, unlinkSync } from "fs";
import { resolve, join } from "path";
import { tmpdir } from "os";

// ---------------------------------------------------------------------------
// DB helpers — identical pattern to monzo-sync.spec.ts / barclays-dedup.spec.ts
// ---------------------------------------------------------------------------

const serverDir = resolve(process.cwd(), "server");

function loadEnvFile(filePath: string): Record<string, string> {
  const content = readFileSync(filePath, "utf-8");
  const result: Record<string, string> = {};
  for (const line of content.split("\n")) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) continue;
    const idx = trimmed.indexOf("=");
    if (idx === -1) continue;
    result[trimmed.slice(0, idx).trim()] = trimmed.slice(idx + 1).trim();
  }
  return result;
}

const testEnv = loadEnvFile(resolve(serverDir, ".env.test"));
const dbFile = testEnv.DATABASE_URL.replace(/^file:/, "");
const dbPath = resolve(serverDir, dbFile).replace(/\\/g, "/");

function runBunScript(script: string): string {
  const tmpFile = join(tmpdir(), `clam-e2e-tx-rendering-${Date.now()}.ts`);
  try {
    writeFileSync(tmpFile, script, "utf-8");
    const result = execSync(`bun ${tmpFile}`, {
      cwd: serverDir,
      env: { ...process.env, ...testEnv },
      stdio: ["pipe", "pipe", "pipe"],
    });
    return result.toString("utf-8").trim();
  } catch (e: any) {
    const stderr = e.stderr?.toString("utf-8") ?? "";
    const stdout = e.stdout?.toString("utf-8") ?? "";
    throw new Error(
      `runBunScript failed\nstdout: ${stdout}\nstderr: ${stderr}\n\nscript:\n${script}`,
    );
  } finally {
    try {
      unlinkSync(tmpFile);
    } catch {
      /* ignore */
    }
  }
}

// ---------------------------------------------------------------------------
// Seed data — 1 category + TX_COUNT transactions, all id-prefixed so cleanup
// is a simple LIKE delete. Only columns without a schema default are
// supplied; owner/reviewed/bucket/externalId/note all fall back to their column
// defaults (Joint / false / null / null / null).
// ---------------------------------------------------------------------------

const TX_COUNT = 1500;
// AG Grid's default row buffer is small; even a tall viewport shouldn't come
// close to rendering the full row set once virtualisation is active.
const MAX_RENDERED_ROWS = 100;

function seedTransactions() {
  const script =
    `import { Database } from 'bun:sqlite';\n` +
    `const db = new Database('${dbPath}'); db.run('PRAGMA busy_timeout = 15000');\n` +
    `db.run(\`INSERT OR IGNORE INTO "Category" (id, name, color) VALUES ('e2e-render-cat', 'E2E Render Category', '#22c55e')\`);\n` +
    `const insert = db.prepare('INSERT INTO "Transaction" (id, description, amount, type, date, categoryId) VALUES (?, ?, ?, ?, ?, ?)');\n` +
    `const insertMany = db.transaction((rows) => { for (const r of rows) insert.run(...r); });\n` +
    `const rows = [];\n` +
    `for (let i = 0; i < ${TX_COUNT}; i++) {\n` +
    `  const day = String(1 + (i % 28)).padStart(2, '0');\n` +
    `  rows.push([\n` +
    `    'e2e-render-' + i,\n` +
    `    'E2E Render Tx ' + i,\n` +
    `    (10 + (i % 500) / 10).toFixed(2),\n` +
    `    i % 5 === 0 ? 'Income' : 'Expense',\n` +
    `    '2026-01-' + day + 'T12:00:00.000Z',\n` +
    `    'e2e-render-cat',\n` +
    `  ]);\n` +
    `}\n` +
    `insertMany(rows);\n` +
    `process.stdout.write('seeded\\n');\n`;
  runBunScript(script);
}

function clearTransactions() {
  runBunScript(
    `import { Database } from 'bun:sqlite';\n` +
      `const db = new Database('${dbPath}'); db.run('PRAGMA busy_timeout = 15000');\n` +
      `db.run("DELETE FROM \\"Transaction\\" WHERE id LIKE 'e2e-render-%'");\n` +
      `db.run("DELETE FROM \\"Category\\" WHERE id = 'e2e-render-cat'");\n`,
  );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

test.describe("Transactions page rendering (virtualisation + mobile/desktop gating)", () => {
  test.beforeAll(() => {
    clearTransactions();
    seedTransactions();
  });

  test.afterAll(() => {
    clearTransactions();
  });

  test("desktop: AG Grid virtualises rows and the mobile card list does not mount", async ({
    page,
  }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto("/transactions");

    // Header total reflects the full seeded count (not the virtualised subset).
    await expect(page.getByText(`${TX_COUNT} entries`)).toBeVisible();

    // Grid has rendered at least one row before we count.
    await expect(page.locator(".ag-row").first()).toBeVisible();

    const renderedRows = await page.locator(".ag-row").count();
    expect(renderedRows).toBeLessThan(MAX_RENDERED_ROWS);
    expect(renderedRows).toBeGreaterThan(0);

    // Mobile card list must be entirely absent from the DOM, not just
    // CSS-hidden — its search input is a unique, unambiguous marker.
    await expect(page.getByPlaceholder("Search description or note…")).toHaveCount(0);
  });

  test("mobile: card list mounts and AG Grid does not mount", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto("/transactions");

    // Mobile card list is present.
    await expect(page.getByPlaceholder("Search description or note…")).toBeVisible();

    // AG Grid must be entirely absent from the DOM, not just CSS-hidden.
    await expect(page.locator(".ag-root-wrapper")).toHaveCount(0);
    await expect(page.locator(".ag-row")).toHaveCount(0);
  });
});

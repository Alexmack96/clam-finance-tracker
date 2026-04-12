/**
 * Monzo sync → stage → process pipeline e2e coverage
 *
 * Assumes:
 *   - global-setup has run migrations + seed on the test DB and written
 *     e2e/.auth/user.json (admin session)
 *   - GET /api/admin/monzo/status is mocked to return configured=true so
 *     the Monzo card renders in "connected" state without Monzo OAuth env vars
 *     (the credential row is also upserted in beforeEach as a belt-and-braces
 *     measure so the real status endpoint would also return connected)
 *
 * Seeding strategy:
 *   - MonzoCredential is inserted directly via bun:sqlite before each test so
 *     the Import page shows the "Sync now" button without a real OAuth flow
 *   - MonzoApiTransaction rows are inserted directly for the process test so we
 *     skip the sync step and avoid needing real Monzo tokens
 *   - POST /api/admin/monzo/sync is intercepted via page.route() for the sync
 *     and duplicate tests, returning fake JSON — the real server endpoint is
 *     never called
 *
 * Tests:
 *   1. Sync — mock returns { imported: 2, duplicates: 0 }; verify "2 rows staged"
 *   2. Process — two pending rows seeded directly; "Process staged" succeeds
 *   3. Duplicate sync — mock returns { imported: 0, duplicates: 2 };
 *      verify "0 rows staged · 2 already existed"
 */

import { test, expect } from "./fixtures.js";
import { execSync } from "child_process";
import { readFileSync, writeFileSync, unlinkSync } from "fs";
import { resolve, join } from "path";
import { tmpdir } from "os";

// ---------------------------------------------------------------------------
// DB helpers — run against the test SQLite file via bun:sqlite inline script
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

// Resolve the SQLite file path from DATABASE_URL (e.g. "file:./test.db" → "./test.db")
const dbFile = testEnv.DATABASE_URL.replace(/^file:/, "");
const dbPath = resolve(serverDir, dbFile).replace(/\\/g, "/");

function runBunScript(script: string): string {
  const tmpFile = join(tmpdir(), `clam-e2e-${Date.now()}.ts`);
  try {
    writeFileSync(tmpFile, script, "utf-8");
    return execSync(`bun ${tmpFile}`, {
      cwd: serverDir,
      env: { ...process.env, ...testEnv },
    }).toString().trim();
  } finally {
    try { unlinkSync(tmpFile); } catch { /* ignore */ }
  }
}

function getAdminUserId(): string {
  const raw = runBunScript(
    `import { Database } from 'bun:sqlite';` +
    `const db = new Database('${dbPath}');` +
    `const r = db.query('SELECT id FROM \\"user\\" WHERE email = ?').get('${testEnv.ADMIN_EMAIL}');` +
    `console.log(r ? r.id : '');`,
  );
  if (!raw) throw new Error(`Admin user not found in test DB (${dbPath})`);
  return raw;
}

function clearMonzoData(_adminUserId: string) {
  const script =
    `import { Database } from 'bun:sqlite';` +
    `const db = new Database('${dbPath}');` +
    `db.run("DELETE FROM monzo_api_transaction WHERE id LIKE 'e2e-%' OR monzoId LIKE 'tx_e2etest%'");` +
    `db.run("DELETE FROM \\"transaction\\" WHERE externalId LIKE 'monzo:tx_e2etest%'");` +
    `db.run("DELETE FROM monzo_credential WHERE id = 'e2e-cred'");`;
  runBunScript(script);
}

function upsertMonzoCredential(adminUserId: string) {
  const futureIso = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString();
  const script =
    `import { Database } from 'bun:sqlite';` +
    `const db = new Database('${dbPath}');` +
    `db.run(\`INSERT OR REPLACE INTO monzo_credential (id, userId, accessToken, refreshToken, expiresAt, accountId, createdAt, updatedAt)` +
    ` VALUES ('e2e-cred', '${adminUserId}', 'fake-access', 'fake-refresh', '${futureIso}', 'acc_test123', datetime('now'), datetime('now'))\`);`;
  runBunScript(script);
}

function seedPendingMonzoTxs() {
  const script =
    `import { Database } from 'bun:sqlite';` +
    `const db = new Database('${dbPath}');` +
    `db.run(\`INSERT OR IGNORE INTO monzo_api_transaction` +
    ` (id, monzoId, created, amountPence, currency, localAmountPence, localCurrency, description, monzoCategory, includeInSpending, accountId, status, importedAt)` +
    ` VALUES` +
    ` ('e2e-stg-01','tx_e2etest001','2026-01-10T12:00:00Z',-500,'GBP',-500,'GBP','TESCO','groceries',1,'acc_test123','pending',datetime('now')),` +
    ` ('e2e-stg-02','tx_e2etest002','2026-01-11T14:00:00Z',-1200,'GBP',-1200,'GBP','UBER','transport',1,'acc_test123','pending',datetime('now'))\`);`;
  runBunScript(script);
}

// ---------------------------------------------------------------------------
// Monzo status mock — makes the Import page render the "connected" Monzo card
// regardless of whether MONZO_* env vars or a real credential exist.
// ---------------------------------------------------------------------------

import type { Page } from "@playwright/test";

async function mockMonzoConnected(page: Page, totalStaged = 0) {
  await page.route("**/api/admin/monzo/status", (route) => {
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        configured: true,
        connected: true,
        accountId: "acc_test123",
        lastSyncedAt: null,
        totalStaged,
      }),
    });
  });
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

test.describe("Monzo sync → stage → process", () => {
  let adminUserId: string;

  test.beforeAll(() => {
    adminUserId = getAdminUserId();
  });

  test.beforeEach(() => {
    clearMonzoData(adminUserId);
    upsertMonzoCredential(adminUserId);
  });

  test.afterAll(() => {
    clearMonzoData(adminUserId);
  });

  // ── 1. Sync ────────────────────────────────────────────────────────────────

  test("sync shows staged count after successful API call", async ({ page }) => {
    await mockMonzoConnected(page);

    await page.route("**/api/admin/monzo/sync", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ imported: 2, duplicates: 0 }),
      });
    });

    await page.goto("/import");

    await expect(page.getByRole("button", { name: "Sync now" })).toBeVisible();
    await page.getByRole("button", { name: "Sync now" }).click();

    // Success banner in the Monzo card: "2 rows staged"
    await expect(page.getByText("2 rows staged")).toBeVisible();
  });

  // ── 2. Process ────────────────────────────────────────────────────────────

  test("process staged drains pending rows and shows transactions added", async ({ page }) => {
    seedPendingMonzoTxs();

    // Status mock — totalStaged=2 so the staging card is visible on load
    await mockMonzoConnected(page, 2);

    await page.goto("/import");

    // Staging card shows 2 pending (the real /staged endpoint returns live data)
    await expect(page.getByText("2 pending")).toBeVisible();

    const processBtn = page.getByRole("button", { name: "Process staged" });
    await expect(processBtn).toBeEnabled();
    await processBtn.click();

    // Success message: "N transactions added"
    await expect(page.getByText(/transactions added/i)).toBeVisible();

    // Staging section collapses or shows 0 pending
    await expect(page.getByText("2 pending")).not.toBeVisible();
  });

  // ── 3. Duplicate sync ─────────────────────────────────────────────────────

  test("duplicate sync shows 'already existed' count", async ({ page }) => {
    await mockMonzoConnected(page);

    await page.route("**/api/admin/monzo/sync", (route) => {
      route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ imported: 0, duplicates: 2 }),
      });
    });

    await page.goto("/import");

    await expect(page.getByRole("button", { name: "Sync now" })).toBeVisible();
    await page.getByRole("button", { name: "Sync now" }).click();

    // "0 rows staged · 2 already existed"
    await expect(page.getByText("0 rows staged")).toBeVisible();
    await expect(page.getByText("2 already existed")).toBeVisible();
  });
});

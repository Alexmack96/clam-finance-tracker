/**
 * Barclays import-route dedup regression test (Option A — real PDF + real HTTP route)
 *
 * Regression: Uploading the same Barclays PDF twice used to create duplicate
 * barclays_transaction rows and, after /process, duplicate transaction rows.
 * The prod bug: "Barclays file uploaded twice → duplicated transactions".
 *
 * What the fix does (server/src/routes/import.ts):
 *   POST /api/admin/import/barclays now assigns each parsed row a content-hash
 *   business key `transactionId` (sha256 of date|description|amount|isCredit,
 *   first 16 hex chars). Within-file same-content repeats get _1, _2, _3 suffixes
 *   (deterministic — sorted before assignment). Existing transactionIds are skipped.
 *
 * Fixture (e2e/fixtures/barclays-dedup.pdf):
 *   7 parsed rows total:
 *     - "E2EDEDUP LIME RIDE £1.75" appears 4 times (→ base, _1, _2, _3)
 *     - "E2EDEDUP COFFEE SHOP £3.50"
 *     - "E2EDEDUP TRAINLINE £42.00"
 *     - "E2EDEDUP GROCERIES TESCO £21.99"
 *   The 4× Lime line also covers the within-file _N suffix round-trip.
 *   N = 7 (EXPECTED_STAGED_COUNT).
 *
 * Tests:
 *   1. Import dedup: first POST → imported=7, duplicates=[].
 *                    second POST (same file) → imported=0, duplicates.length=7.
 *   2. Process dedup: after two uploads (only 7 rows staged), /process creates
 *                     exactly 7 transaction rows with E2EDEDUP descriptions, not 14.
 *
 * Isolation: beforeEach + afterAll delete only rows matching E2EDEDUP%.
 */

import { test, expect } from "./fixtures.js";
import { execSync } from "child_process";
import { readFileSync, writeFileSync, unlinkSync } from "fs";
import { resolve, join } from "path";
import { tmpdir } from "os";

// ---------------------------------------------------------------------------
// DB helpers — identical pattern to monzo-sync.spec.ts
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
  const tmpFile = join(tmpdir(), `clam-e2e-barclays-${Date.now()}.ts`);
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
    throw new Error(`runBunScript failed\nstdout: ${stdout}\nstderr: ${stderr}\n\nscript:\n${script}`);
  } finally {
    try { unlinkSync(tmpFile); } catch { /* ignore */ }
  }
}

// ---------------------------------------------------------------------------
// Fixture
// ---------------------------------------------------------------------------

const FIXTURE_PATH = resolve(process.cwd(), "e2e/fixtures/barclays-dedup.pdf");

// 7 distinct staged rows: 4 Lime + Coffee + Trainline + Groceries
const EXPECTED_STAGED_COUNT = 7;

// ---------------------------------------------------------------------------
// DB isolation helpers
// ---------------------------------------------------------------------------

function clearTestData() {
  runBunScript(
    `import { Database } from 'bun:sqlite';\n` +
    `const db = new Database('${dbPath}');\n` +
    `db.run("DELETE FROM barclays_transaction WHERE description LIKE 'E2EDEDUP%'");\n` +
    `db.run("DELETE FROM \\"transaction\\" WHERE description LIKE 'E2EDEDUP%'");\n`,
  );
}

function countTransactionRows(): number {
  const out = runBunScript(
    `import { Database } from 'bun:sqlite';\n` +
    `const db = new Database('${dbPath}');\n` +
    `const r = db.query("SELECT COUNT(*) as cnt FROM \\"transaction\\" WHERE description LIKE 'E2EDEDUP%'").get();\n` +
    `process.stdout.write(String(r.cnt) + '\\n');\n`,
  );
  return parseInt(out, 10);
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

test.describe("Barclays dedup — real import route, Option A", () => {
  test.beforeEach(() => {
    clearTestData();
  });

  test.afterAll(() => {
    clearTestData();
  });

  test("first upload stages N rows; second upload of same file produces 0 imported and N duplicates", async ({ page }) => {
    const pdfBuffer = readFileSync(FIXTURE_PATH);

    // ── First upload ──────────────────────────────────────────────────────────
    const res1 = await page.request.post("/api/admin/import/barclays", {
      multipart: {
        file: {
          name: "barclays-dedup.pdf",
          mimeType: "application/pdf",
          buffer: pdfBuffer,
        },
        owner: "Alex",
      },
    });
    expect(res1.ok(), `First upload failed: ${await res1.text()}`).toBe(true);

    const body1 = await res1.json() as { imported: number; duplicates: string[] };
    expect(body1.imported).toBe(EXPECTED_STAGED_COUNT);
    expect(body1.duplicates).toHaveLength(0);

    // ── Second upload (same file — core regression assertion) ─────────────────
    const res2 = await page.request.post("/api/admin/import/barclays", {
      multipart: {
        file: {
          name: "barclays-dedup.pdf",
          mimeType: "application/pdf",
          buffer: pdfBuffer,
        },
        owner: "Alex",
      },
    });
    expect(res2.ok(), `Second upload failed: ${await res2.text()}`).toBe(true);

    const body2 = await res2.json() as { imported: number; duplicates: string[] };
    // Pre-fix: this would return imported=7, duplicates=[]. Post-fix: imported=0, duplicates=7 ids.
    expect(body2.imported).toBe(0);
    expect(body2.duplicates).toHaveLength(EXPECTED_STAGED_COUNT);
  });

  test("processing double-upload attempt creates exactly N transactions, not 2N", async ({ page }) => {
    const pdfBuffer = readFileSync(FIXTURE_PATH);

    // Upload the same file twice — dedup means only N rows are staged
    for (let i = 0; i < 2; i++) {
      await page.request.post("/api/admin/import/barclays", {
        multipart: {
          file: {
            name: "barclays-dedup.pdf",
            mimeType: "application/pdf",
            buffer: pdfBuffer,
          },
          owner: "Alex",
        },
      });
    }

    // Process all pending staged rows
    const processRes = await page.request.post("/api/admin/process");
    expect(processRes.ok(), `Process failed: ${await processRes.text()}`).toBe(true);

    // Assert exactly N transaction rows, not 2N
    const txCount = countTransactionRows();
    expect(txCount).toBe(EXPECTED_STAGED_COUNT);
  });
});

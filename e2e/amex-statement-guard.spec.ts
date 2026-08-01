/**
 * Amex statement upload guard — POST /api/admin/import/amex
 *
 * Regression + new-behaviour coverage for server/src/routes/import.ts.
 *
 * There are TWO separate 409s on this route — don't conflate them:
 *
 *   1. contentHash guard (pre-existing): re-uploading the byte-identical PDF.
 *      Keyed on StatementFile.contentHash (SHA-256 of the raw bytes).
 *      Message: "This exact PDF was already uploaded on ...".
 *
 *   2. all-rows-staged guard (new, this spec's focus): the bytes are NOT a
 *      byte-identical re-upload (no StatementFile row has that contentHash),
 *      but every row `parseAmexBuffer` extracts already exists in
 *      amex_transaction. Before this guard existed, this case still created a
 *      StatementFile row and wrote the PDF to the statements volume, with zero
 *      linked rows — an orphan. Now `partitionAmexRows` (pure, no writes) runs
 *      BEFORE anything is persisted, and if toInsert.length === 0 the route
 *      responds 409 and creates nothing.
 *      Message: "Every transaction on this statement (<n>) is already
 *      imported, so nothing was stored.", body.duplicates = n (a number).
 *
 * To exercise guard 2 without needing a second real PDF with different bytes,
 * this spec re-derives the same setup the prod bug required: upload the real
 * fixture once (so its rows exist in amex_transaction), then remove ONLY the
 * StatementFile row that upload created (nulling the FK on the amex_transaction
 * rows first, so they survive) and delete its PDF from disk. Re-uploading the
 * identical bytes then finds no contentHash match (guard 1 stays quiet) but
 * every parsed row still exists (guard 2 fires) — precisely the "bytes differ,
 * rows already staged" case, without fabricating a second PDF.
 *
 * Isolation:
 *   - server/.env.test sets STATEMENTS_DIR=./statements/e2e-test (nested under
 *     the already-gitignored server/statements/ tree) specifically so this spec
 *     can inspect on-disk PDF writes without touching a dev's real statements.
 *   - beforeEach wipes amex_transaction, statement_file (bank='amex'), and any
 *     "Transaction" rows with an amex: externalId, plus the whole statements
 *     dir. No other e2e spec touches Amex tables, so a full wipe is safe.
 *
 * Fixture: e2e/resources/statements/2026-07-24-amex.pdf — a real Amex
 * statement already used (and proven to reconcile) by
 * server/src/routes/amex-statement.test.ts.
 *
 * Tests:
 *   1. Fresh upload succeeds, stages rows, creates exactly one StatementFile,
 *      writes exactly one PDF.
 *   2. Re-uploading the identical bytes hits the pre-existing contentHash 409
 *      — regression guard, no new StatementFile/PDF.
 *   3. A statement whose rows are all already staged hits the new guard: 409,
 *      correct message + duplicates count, NO StatementFile row, NO PDF.
 *   4. After /process, each resulting Transaction carries the statementFileId
 *      of the statement it came from (new column, migration
 *      20260801000000_transaction_statement_file).
 */

import { test, expect } from "./fixtures.js";
import { execSync } from "child_process";
import { createHash } from "crypto";
import { readFileSync, writeFileSync, unlinkSync, existsSync, readdirSync, rmSync } from "fs";
import { resolve, join } from "path";
import { tmpdir } from "os";

// ---------------------------------------------------------------------------
// DB / env helpers — identical pattern to barclays-dedup.spec.ts
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

// Mirrors defaultStatementsDir() in server/src/lib/statementStorage.ts, given
// STATEMENTS_DIR is set in server/.env.test (added specifically for this spec).
//
// Demanded rather than defaulted: `server/.env.test` is gitignored, so a fresh
// clone won't have the line. Falling back to the derived default would point this
// spec at `server/statements` — the same directory a local dev server uploads
// into — and the cleanup below deletes that directory recursively. Fail loudly
// instead of eating someone's PDFs.
if (!testEnv.STATEMENTS_DIR)
  throw new Error(
    "STATEMENTS_DIR is not set in server/.env.test. Add `STATEMENTS_DIR=./statements/e2e-test` " +
      "before running this spec — without it the cleanup would delete server/statements.",
  );
const statementsDir = resolve(serverDir, testEnv.STATEMENTS_DIR);

function runBunScript(script: string): string {
  const tmpFile = join(tmpdir(), `clam-e2e-amex-guard-${Date.now()}-${Math.random().toString(36).slice(2)}.ts`);
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
    try {
      unlinkSync(tmpFile);
    } catch {
      /* ignore */
    }
  }
}

function amexStatementFileCount(): number {
  const out = runBunScript(
    `import { Database } from 'bun:sqlite';\n` +
      `const db = new Database('${dbPath}'); db.run('PRAGMA busy_timeout = 15000');\n` +
      `const r = db.query("SELECT COUNT(*) as cnt FROM statement_file WHERE bank = 'amex'").get();\n` +
      `process.stdout.write(String(r.cnt) + '\\n');\n`,
  );
  return parseInt(out, 10);
}

function amexTransactionCount(): number {
  const out = runBunScript(
    `import { Database } from 'bun:sqlite';\n` +
      `const db = new Database('${dbPath}'); db.run('PRAGMA busy_timeout = 15000');\n` +
      `const r = db.query("SELECT COUNT(*) as cnt FROM amex_transaction").get();\n` +
      `process.stdout.write(String(r.cnt) + '\\n');\n`,
  );
  return parseInt(out, 10);
}

/** Null the FK on amex_transaction rows pointing at this StatementFile (so the
 *  rows survive), then delete the StatementFile row itself. Simulates "rows
 *  already staged, no source file recorded for them". */
function detachAndDeleteStatementFile(statementFileId: string): void {
  runBunScript(
    `import { Database } from 'bun:sqlite';\n` +
      `const db = new Database('${dbPath}'); db.run('PRAGMA busy_timeout = 15000');\n` +
      `db.run("UPDATE amex_transaction SET statementFileId = NULL WHERE statementFileId = '${statementFileId}'");\n` +
      `db.run("DELETE FROM statement_file WHERE id = '${statementFileId}'");\n`,
  );
}

function wipeAmexData(): void {
  runBunScript(
    `import { Database } from 'bun:sqlite';\n` +
      `const db = new Database('${dbPath}'); db.run('PRAGMA busy_timeout = 15000');\n` +
      `db.run("DELETE FROM \\"Transaction\\" WHERE externalId LIKE 'amex:%'");\n` +
      `db.run("DELETE FROM amex_transaction");\n` +
      `db.run("DELETE FROM statement_file WHERE bank = 'amex'");\n`,
  );
}

interface LinkedTxRow {
  externalId: string;
  statementFileId: string | null;
}

function amexLinkedTransactionRows(): LinkedTxRow[] {
  const out = runBunScript(
    `import { Database } from 'bun:sqlite';\n` +
      `const db = new Database('${dbPath}'); db.run('PRAGMA busy_timeout = 15000');\n` +
      `const rows = db.query("SELECT externalId, statementFileId FROM \\"Transaction\\" WHERE externalId LIKE 'amex:%'").all();\n` +
      `process.stdout.write(JSON.stringify(rows));\n`,
  );
  return out ? JSON.parse(out) : [];
}

// ---------------------------------------------------------------------------
// Disk helpers
// ---------------------------------------------------------------------------

function countPdfFiles(dir: string): number {
  if (!existsSync(dir)) return 0;
  let count = 0;
  const walk = (d: string) => {
    for (const entry of readdirSync(d, { withFileTypes: true })) {
      const full = join(d, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.isFile() && entry.name.toLowerCase().endsWith(".pdf")) count++;
    }
  };
  walk(dir);
  return count;
}

function wipeStatementsDir(): void {
  rmSync(statementsDir, { recursive: true, force: true });
}

// ---------------------------------------------------------------------------
// Fixture
// ---------------------------------------------------------------------------

const FIXTURE_PATH = resolve(process.cwd(), "e2e/resources/statements/2026-07-24-amex.pdf");
const FIXTURE_BUFFER = readFileSync(FIXTURE_PATH);
const OWNER = "Alex";

async function uploadFixture(page: import("@playwright/test").Page) {
  return page.request.post("/api/admin/import/amex", {
    multipart: {
      file: {
        name: "2026-07-24-amex.pdf",
        mimeType: "application/pdf",
        buffer: FIXTURE_BUFFER,
      },
      owner: OWNER,
    },
  });
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

test.describe("Amex import guards — /api/admin/import/amex", () => {
  test.beforeEach(() => {
    wipeAmexData();
    wipeStatementsDir();
  });

  test.afterAll(() => {
    wipeAmexData();
    wipeStatementsDir();
  });

  test("fresh upload succeeds and creates exactly one StatementFile + one PDF", async ({ page }) => {
    const res = await uploadFixture(page);
    expect(res.ok(), `Upload failed: ${await res.text()}`).toBe(true);

    const body = (await res.json()) as { imported: number; duplicates: string[]; statementFileId: string };
    expect(body.imported).toBeGreaterThan(0);
    expect(body.duplicates).toEqual([]);
    expect(typeof body.statementFileId).toBe("string");

    expect(amexStatementFileCount()).toBe(1);
    expect(amexTransactionCount()).toBe(body.imported);
    expect(countPdfFiles(statementsDir)).toBe(1);
  });

  test("re-uploading identical bytes hits the contentHash 409 (regression guard)", async ({ page }) => {
    const first = await uploadFixture(page);
    expect(first.ok(), `First upload failed: ${await first.text()}`).toBe(true);
    const firstBody = (await first.json()) as { statementFileId: string };

    const second = await uploadFixture(page);
    expect(second.status()).toBe(409);
    const secondBody = (await second.json()) as { error: string; statementFileId: string };
    expect(secondBody.error).toMatch(/^This exact PDF was already uploaded on/);
    expect(secondBody.statementFileId).toBe(firstBody.statementFileId);

    // No new StatementFile row or PDF from the rejected re-upload.
    expect(amexStatementFileCount()).toBe(1);
    expect(countPdfFiles(statementsDir)).toBe(1);
  });

  test("a statement whose rows are all already staged is rejected with no StatementFile row and no PDF written", async ({
    page,
  }) => {
    // Stage the fixture's rows for real once, then strip away everything that
    // would make a re-upload look like a byte-identical repeat: null the FK on
    // the staged rows (so they survive) and delete the StatementFile row that
    // owned them. The PDF that upload wrote is no longer of interest either,
    // so wipe the whole statements dir — what matters from here is whether the
    // NEXT upload attempt writes anything back.
    const first = await uploadFixture(page);
    expect(first.ok(), `Setup upload failed: ${await first.text()}`).toBe(true);
    const { imported: stagedCount, statementFileId } = (await first.json()) as {
      imported: number;
      statementFileId: string;
    };
    expect(stagedCount).toBeGreaterThan(0);

    detachAndDeleteStatementFile(statementFileId);
    wipeStatementsDir();

    expect(amexStatementFileCount()).toBe(0);
    expect(amexTransactionCount()).toBe(stagedCount); // rows survived the detach
    expect(countPdfFiles(statementsDir)).toBe(0);

    // Re-upload the SAME bytes. No StatementFile row has this contentHash any
    // more, so the contentHash guard stays quiet — but every parsed row is
    // still present in amex_transaction, so the new all-duplicate guard fires.
    const res = await uploadFixture(page);
    expect(res.status()).toBe(409);

    const body = (await res.json()) as { error: string; duplicates: number };
    expect(body.error).toBe(
      `Every transaction on this statement (${stagedCount}) is already imported, so nothing was stored.`,
    );
    expect(body.duplicates).toBe(stagedCount);

    // The whole point: no orphaned StatementFile row, no orphaned PDF.
    expect(amexStatementFileCount()).toBe(0);
    expect(countPdfFiles(statementsDir)).toBe(0);
  });

  test("processed Amex transactions carry the statementFileId of their source statement", async ({ page }) => {
    const upload = await uploadFixture(page);
    expect(upload.ok(), `Upload failed: ${await upload.text()}`).toBe(true);
    const { imported: stagedCount, statementFileId } = (await upload.json()) as {
      imported: number;
      statementFileId: string;
    };
    expect(stagedCount).toBeGreaterThan(0);

    const processRes = await page.request.post("/api/admin/process");
    expect(processRes.ok(), `Process failed: ${await processRes.text()}`).toBe(true);

    const linked = amexLinkedTransactionRows();
    expect(linked).toHaveLength(stagedCount);
    for (const row of linked) {
      expect(row.statementFileId).toBe(statementFileId);
    }
  });
});

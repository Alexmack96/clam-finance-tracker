/**
 * Users page e2e coverage
 *
 * Create User — happy path:
 *   - Clicking "Create User" opens the dialog
 *   - Filling name, email, password and submitting creates the user
 *   - Modal closes and the new user appears in the table
 *
 * Preconditions:
 *   - global-setup seeds the DB and writes e2e/.auth/user.json (admin session)
 *   - `page` fixture carries stored admin auth state
 */

import { test, expect } from "./fixtures.js";
import { execSync } from "child_process";
import { readFileSync, writeFileSync, unlinkSync } from "fs";
import { resolve, join } from "path";
import { tmpdir } from "os";

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

function deleteTestNewusers() {
  const tmpFile = join(tmpdir(), `clam-e2e-users-${Date.now()}.ts`);
  const script =
    `import { Database } from 'bun:sqlite';` +
    `const db = new Database('${dbPath}');` +
    `db.run("DELETE FROM \\"user\\" WHERE name = 'Test Newuser'");`;
  try {
    writeFileSync(tmpFile, script, "utf-8");
    execSync(`bun ${tmpFile}`, { cwd: serverDir, env: { ...process.env, ...testEnv } });
  } finally {
    try { unlinkSync(tmpFile); } catch { /* ignore */ }
  }
}

test.describe("Users — create user", () => {
  test.beforeEach(() => { deleteTestNewusers(); });
  test.afterAll(() => { deleteTestNewusers(); });

  test("creates a new user and shows them in the table", async ({ page }) => {
    const email = `newuser-${Date.now()}@example.com`;

    await page.goto("/users");

    await page.getByRole("button", { name: "Create User" }).click();

    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();

    await dialog.getByLabel("Name").fill("Test Newuser");
    await dialog.getByLabel("Email").fill(email);
    await dialog.getByLabel("Password").fill("password123");

    await dialog.getByRole("button", { name: "Create User" }).click();

    await expect(dialog).not.toBeVisible();
    await expect(page.getByRole("cell", { name: "Test Newuser" })).toBeVisible();
  });
});

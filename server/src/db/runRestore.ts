import { config } from "dotenv";
config({ path: import.meta.dirname + "/../../../.env" });

import { readFileSync } from "fs";
import { join } from "path";

const PROD_URL = process.env.PROD_URL;
const PROD_EMAIL = process.env.PROD_EMAIL ?? process.env.ADMIN_EMAIL;
const PROD_PASS = process.env.PROD_PASS ?? process.env.ADMIN_PASSWORD;

if (!PROD_URL) {
  console.error("Missing PROD_URL. Usage:\n  PROD_URL=https://your-app.railway.app bun run server/src/db/runRestore.ts");
  process.exit(1);
}
if (!PROD_EMAIL || !PROD_PASS) {
  console.error("Missing PROD_EMAIL / PROD_PASS (or ADMIN_EMAIL / ADMIN_PASSWORD in .env)");
  process.exit(1);
}

async function main() {
  console.log(`Signing in to ${PROD_URL}...`);

  const loginRes = await fetch(`${PROD_URL}/api/auth/sign-in/email`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email: PROD_EMAIL, password: PROD_PASS }),
  });

  if (!loginRes.ok) {
    console.error("Login failed:", loginRes.status, await loginRes.text());
    process.exit(1);
  }

  // Collect all session cookies from the login response
  const rawCookies: string[] = (loginRes.headers as any).getSetCookie?.()
    ?? (loginRes.headers.get("set-cookie") ?? "").split(/,(?=\s*\w+=)/).filter(Boolean);

  if (rawCookies.length === 0) {
    console.error("No Set-Cookie headers received. Response headers:", Object.fromEntries(loginRes.headers.entries()));
    process.exit(1);
  }

  const cookieHeader = rawCookies.map((c) => c.split(";")[0]).join("; ");
  console.log("Signed in.");

  const dataPath = join(import.meta.dirname, "../../../restore-data.json");
  let jsonStr: string;
  try {
    jsonStr = readFileSync(dataPath, "utf-8");
  } catch {
    console.error(`restore-data.json not found at ${dataPath}. Run the export script first:\n  bun run server/src/db/exportForRestore.ts`);
    process.exit(1);
  }

  const data = JSON.parse(jsonStr);
  const totalRows = Object.values(data).reduce((sum, rows) => sum + (rows as unknown[]).length, 0);
  console.log(`Uploading ${totalRows} rows across ${Object.keys(data).length} tables...`);

  const formData = new FormData();
  formData.append("data", new Blob([jsonStr], { type: "application/json" }), "restore-data.json");

  const restoreRes = await fetch(`${PROD_URL}/api/admin/db-restore`, {
    method: "POST",
    headers: { Cookie: cookieHeader },
    body: formData,
  });

  if (!restoreRes.ok) {
    console.error("Restore failed:", restoreRes.status, await restoreRes.text());
    process.exit(1);
  }

  const result = await restoreRes.json() as { success: boolean; counts: Record<string, number> };
  console.log("\nRestore complete! Rows inserted:");
  for (const [table, count] of Object.entries(result.counts)) {
    console.log(`  ${table}: ${count}`);
  }
}

main().catch((err) => { console.error(err); process.exit(1); });

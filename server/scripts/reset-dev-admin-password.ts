// Restoring prod data into dev.db (see /db-restore) brings over the admin
// account's real prod password hash, which silently breaks local login since
// it no longer matches ADMIN_PASSWORD in .env. Run this after every restore to
// reset the credential back to the local dev password.
import { config } from "dotenv";
import { resolve } from "path";
import { fileURLToPath } from "url";
const here = fileURLToPath(new URL(".", import.meta.url));
config({ path: resolve(here, "../../.env") });

const { db } = await import("../src/db/client.js");
const { hashPassword } = await import("better-auth/crypto");
const { env } = await import("../src/config/env.js");

const user = await db.user.findUnique({ where: { email: env.ADMIN_EMAIL } });
if (!user) {
  console.error(`No user found for ADMIN_EMAIL (${env.ADMIN_EMAIL}) — nothing to reset.`);
  process.exit(1);
}

const hashed = await hashPassword(env.ADMIN_PASSWORD);
const { count } = await db.account.updateMany({
  where: { userId: user.id, providerId: "credential" },
  data: { password: hashed },
});

if (count === 0) {
  console.error(`No credential account found for ${env.ADMIN_EMAIL} — nothing to reset.`);
  process.exit(1);
}

console.log(`Reset local dev password for ${env.ADMIN_EMAIL} to match ADMIN_PASSWORD in .env.`);
await db.$disconnect();

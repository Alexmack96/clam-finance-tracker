import "dotenv/config";
import { db } from "./client.js";
import { hashPassword } from "better-auth/crypto";
import { randomUUID } from "crypto";
import { env } from "../config/env.js";

async function main() {
  const existing = await db.user.findUnique({ where: { email: env.ADMIN_EMAIL } });
  if (existing) {
    console.log("Seed already applied.");
    return;
  }

  const userId = randomUUID();
  const accountId = randomUUID();
  const hashedPassword = await hashPassword(env.ADMIN_PASSWORD);

  await db.user.create({
    data: {
      id: userId,
      email: env.ADMIN_EMAIL,
      name: "Admin",
    },
  });

  await db.account.create({
    data: {
      id: accountId,
      accountId: userId,
      providerId: "credential",
      userId,
      password: hashedPassword,
    },
  });

  console.log(`Seeded admin user: ${env.ADMIN_EMAIL}`);
}

main()
  .catch(console.error)
  .finally(() => db.$disconnect());

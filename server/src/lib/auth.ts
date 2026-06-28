import { betterAuth } from "better-auth";
import { prismaAdapter } from "better-auth/adapters/prisma";
import { db } from "../db/client.js";
import { env } from "../config/env.js";

export const auth = betterAuth({
  database: prismaAdapter(db, { provider: "sqlite" }),
  trustedOrigins: env.TRUSTED_ORIGINS.split(",").map((o) => o.trim()),
  emailAndPassword: { enabled: true, disableSignUp: true },
  user: {
    additionalFields: {
      // Which person (Alex/Casey) this login represents — drives per-person page defaults.
      owner: { type: "string", required: false, input: false },
    },
  },
});

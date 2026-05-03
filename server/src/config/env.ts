import { z } from "zod";

const envSchema = z.object({
  NODE_ENV: z.enum(["development", "production", "test"]).default("development"),
  PORT: z.coerce.number().default(3000),
  DATABASE_URL: z.string(),
  BETTER_AUTH_SECRET: z.string().min(32, "BETTER_AUTH_SECRET must be at least 32 characters"),
  BETTER_AUTH_URL: z.string().default("http://localhost:3000"),
  TRUSTED_ORIGINS: z.string().default("http://localhost:5173"),
  SENDGRID_API_KEY: z.string().optional(),
  SENDGRID_FROM_EMAIL: z.string().optional(),
  SENDGRID_WEBHOOK_KEY: z.string().optional(),
  ANTHROPIC_API_KEY: z.string().optional(),
  SENTRY_DSN: z.string().optional(),
  SENTRY_ENVIRONMENT: z.string().optional(),
  MONZO_CLIENT_ID: z.string().optional(),
  MONZO_CLIENT_SECRET: z.string().optional(),
  MONZO_REDIRECT_URI: z.string().url().optional(),
  PLAID_CLIENT_ID: z.string().optional(),
  PLAID_SECRET: z.string().optional(),
  PLAID_ENV: z.enum(["sandbox", "production"]).default("sandbox"),
  ADMIN_EMAIL: z.string().email(),
  ADMIN_PASSWORD: z.string().min(8),
});

export const env = envSchema.parse(process.env);

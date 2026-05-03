import "dotenv/config";
import * as Sentry from "@sentry/node";
import express from "express";
import cors from "cors";
import helmet from "helmet";
import rateLimit from "express-rate-limit";
import { toNodeHandler } from "better-auth/node";
import { auth } from "./lib/auth.js";
import { env } from "./config/env.js";
import { errorHandler } from "./middleware/errorHandler.js";
import { requireAuth, requireAdmin } from "./middleware/auth.js";
import { initSystemCategories, mapMonzoCategories, migrateOwners, consolidateFoodCategories, migrateTakeout } from "./routes/admin.js";
import { usersRouter } from "./routes/users.js";
import { importRouter } from "./routes/import.js";
import { categoriesRouter } from "./routes/categories.js";
import { transactionsRouter } from "./routes/transactions.js";
import { dashboardRouter } from "./routes/dashboard.js";
import { utilitiesRouter } from "./routes/utilities.js";
import { investmentsRouter } from "./routes/investments.js";
import { tabsRouter } from "./routes/tabs.js";
import { notesRouter } from "./routes/notes.js";
import { monzoRouter } from "./routes/monzo.js";
import { plaidRouter } from "./routes/plaid.js";

Sentry.init({ dsn: env.SENTRY_DSN, sendDefaultPii: true });

const app = express();

const trustedOrigins = env.TRUSTED_ORIGINS.split(",").map((o) => o.trim());

app.use(
  cors({
    origin: trustedOrigins,
    credentials: true,
  })
);

app.use(helmet());

app.use(express.json({ limit: "50kb" }));
app.use(express.urlencoded({ extended: true, limit: "50kb" }));

if (env.NODE_ENV === "production") {
  const authLimiter = rateLimit({
    windowMs: 15 * 60 * 1000,
    max: 20,
    standardHeaders: true,
    legacyHeaders: false,
  });
  app.post("/api/auth/sign-in/*path", authLimiter);
}
app.all("/api/auth/*path", toNodeHandler(auth));

app.get("/api/health", (_req, res) => {
  res.json({ status: "ok" });
});

app.post("/api/admin/sentry-test", requireAuth, requireAdmin, (_req, _res) => {
  throw new Error("Server Sentry test error");
});

app.get("/api/me", requireAuth, (req, res) => {
  const { id, email, name, role } = req.user!;
  res.json({ user: { id, email, name, role } });
});

app.use("/api/admin/users", requireAuth, requireAdmin, usersRouter);
app.use("/api/admin", requireAuth, requireAdmin, importRouter);
app.use("/api/categories", requireAuth, categoriesRouter);
app.use("/api/transactions", requireAuth, transactionsRouter);
app.use("/api/dashboard", requireAuth, dashboardRouter);
app.use("/api/utilities", requireAuth, utilitiesRouter);
app.use("/api/investments", requireAuth, investmentsRouter);
app.use("/api/tabs", requireAuth, tabsRouter);
app.use("/api/notes", requireAuth, notesRouter);
app.use("/api/admin/monzo", monzoRouter);
app.use("/api/admin/plaid", plaidRouter);

Sentry.setupExpressErrorHandler(app);
app.use(errorHandler);

app.listen(env.PORT, async () => {
  console.log(`Backend running on port ${env.PORT}`);
  await initSystemCategories();
  await consolidateFoodCategories();
  await migrateTakeout();
  await mapMonzoCategories();
  await migrateOwners();
});

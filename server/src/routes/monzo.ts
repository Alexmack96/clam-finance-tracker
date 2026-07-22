import { Router } from "express";
import { randomBytes } from "crypto";
import * as Sentry from "@sentry/node";
import { db } from "../db/client.js";
import { env } from "../config/env.js";
import { requireAuth } from "../middleware/auth.js";
import type { MonzoCredential, Prisma } from "../generated/prisma/index.js";

export const monzoRouter = Router();

// ─── Helpers ─────────────────────────────────────────────────────────────────

function monzoGuard(res: import("express").Response): boolean {
  if (!env.MONZO_CLIENT_ID || !env.MONZO_CLIENT_SECRET || !env.MONZO_REDIRECT_URI) {
    res.status(503).json({
      error:
        "Monzo OAuth not configured — set MONZO_CLIENT_ID, MONZO_CLIENT_SECRET, MONZO_REDIRECT_URI in .env",
    });
    return false;
  }
  return true;
}

async function ensureFreshToken(credential: MonzoCredential): Promise<MonzoCredential> {
  const bufferMs = 5 * 60 * 1000; // refresh 5 min before expiry
  if (credential.expiresAt.getTime() - Date.now() > bufferMs) return credential;

  const resp = await fetch("https://api.monzo.com/oauth2/token", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "refresh_token",
      client_id: env.MONZO_CLIENT_ID!,
      client_secret: env.MONZO_CLIENT_SECRET!,
      refresh_token: credential.refreshToken,
    }),
  });

  if (!resp.ok) {
    const body = await resp.text();
    // Refresh token invalidated — user needs to reconnect
    await db.monzoCredential.delete({ where: { id: credential.id } });
    throw Object.assign(
      new Error(`Monzo token expired and could not be refreshed — please reconnect: ${body}`),
      { status: 401 },
    );
  }

  const data = (await resp.json()) as {
    access_token: string;
    refresh_token: string;
    expires_in: number;
  };
  return db.monzoCredential.update({
    where: { id: credential.id },
    data: {
      accessToken: data.access_token,
      refreshToken: data.refresh_token,
      expiresAt: new Date(Date.now() + data.expires_in * 1000),
    },
  });
}

// ─── GET /api/admin/monzo/status ─────────────────────────────────────────────

monzoRouter.get("/status", requireAuth, async (_req, res) => {
  const configured = !!(env.MONZO_CLIENT_ID && env.MONZO_CLIENT_SECRET && env.MONZO_REDIRECT_URI);
  const credential = await db.monzoCredential.findFirst({
    select: { accountId: true },
  });
  const latest = await db.monzoApiTransaction.findFirst({
    orderBy: { created: "desc" },
    select: { created: true },
  });
  const totalStaged = await db.monzoApiTransaction.count();

  res.json({
    configured,
    connected: !!credential,
    accountId: credential?.accountId ?? null,
    lastSyncedAt: latest?.created ?? null,
    totalStaged,
  });
});

// ─── GET /api/admin/monzo/auth — initiates OAuth ──────────────────────────────

monzoRouter.get("/auth", requireAuth, async (req, res) => {
  if (!monzoGuard(res)) return;

  const state = randomBytes(16).toString("hex");

  await db.verification.create({
    data: {
      id: randomBytes(8).toString("hex"),
      identifier: "monzo-oauth-state",
      value: JSON.stringify({ state, userId: req.user!.id }),
      expiresAt: new Date(Date.now() + 10 * 60 * 1000),
      updatedAt: new Date(),
    },
  });

  const url = new URL("https://auth.monzo.com/");
  url.searchParams.set("client_id", env.MONZO_CLIENT_ID!);
  url.searchParams.set("redirect_uri", env.MONZO_REDIRECT_URI!);
  url.searchParams.set("response_type", "code");
  url.searchParams.set("state", state);

  console.log("[monzo:auth] redirecting to:", url.toString());
  res.redirect(302, url.toString());
});

// ─── GET /api/admin/monzo/callback — NOT behind requireAuth ───────────────────

monzoRouter.get("/callback", async (req, res) => {
  console.log("[monzo:callback] hit", req.query);
  if (!monzoGuard(res)) return;

  const { code, state, error } = req.query as Record<string, string>;
  const clientUrl = env.TRUSTED_ORIGINS.split(",")[0].trim();

  if (error || !state || !code) {
    console.log(
      "[monzo:callback] missing params — error:",
      error,
      "state:",
      !!state,
      "code:",
      !!code,
    );
    res.redirect(302, `${clientUrl}/import?monzo=error`);
    return;
  }

  // Verify state
  const verification = await db.verification.findFirst({
    where: { identifier: "monzo-oauth-state", expiresAt: { gt: new Date() } },
    orderBy: { createdAt: "desc" },
  });

  if (!verification) {
    console.log("[monzo:callback] no valid state verification found in DB");
    res.redirect(302, `${clientUrl}/import?monzo=error`);
    return;
  }

  let payload: { state: string; userId: string };
  try {
    payload = JSON.parse(verification.value);
  } catch {
    console.log("[monzo:callback] failed to parse verification value:", verification.value);
    res.redirect(302, `${clientUrl}/import?monzo=error`);
    return;
  }

  if (payload.state !== state) {
    console.log("[monzo:callback] state mismatch — expected:", payload.state, "got:", state);
    res.redirect(302, `${clientUrl}/import?monzo=error`);
    return;
  }

  await db.verification.delete({ where: { id: verification.id } });
  console.log("[monzo:callback] state verified, exchanging code for tokens");

  // Exchange code for tokens
  const tokenResp = await fetch("https://api.monzo.com/oauth2/token", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "authorization_code",
      client_id: env.MONZO_CLIENT_ID!,
      client_secret: env.MONZO_CLIENT_SECRET!,
      redirect_uri: env.MONZO_REDIRECT_URI!,
      code,
    }),
  });

  if (!tokenResp.ok) {
    const body = await tokenResp.text();
    console.log("[monzo:callback] token exchange failed:", tokenResp.status, body);
    res.redirect(302, `${clientUrl}/import?monzo=error`);
    return;
  }

  console.log("[monzo:callback] tokens received, saving credential");
  const tokens = (await tokenResp.json()) as {
    access_token: string;
    refresh_token: string;
    expires_in: number;
  };

  await db.monzoCredential.upsert({
    where: { userId: payload.userId },
    create: {
      id: randomBytes(8).toString("hex"),
      userId: payload.userId,
      accessToken: tokens.access_token,
      refreshToken: tokens.refresh_token,
      expiresAt: new Date(Date.now() + tokens.expires_in * 1000),
      updatedAt: new Date(),
    },
    update: {
      accessToken: tokens.access_token,
      refreshToken: tokens.refresh_token,
      expiresAt: new Date(Date.now() + tokens.expires_in * 1000),
      accountId: null, // will be resolved on first sync
    },
  });

  console.log("[monzo:callback] credential saved, redirecting to import page");
  res.redirect(302, `${clientUrl}/import?monzo=connected`);
});

// ─── POST /api/admin/monzo/sync ───────────────────────────────────────────────

type MonzoTx = {
  id: string;
  created: string;
  settled: string;
  amount: number;
  currency: string;
  local_amount: number;
  local_currency: string;
  description: string;
  notes: string;
  category: string;
  decline_reason?: string | null;
  scheme?: string;
  include_in_spending: boolean;
  account_id: string;
  merchant?: { name?: string; emoji?: string; address?: { short_formatted?: string } } | null;
};

// Account types we pull transactions from. uk_monzo_flex is undocumented but
// real — confirmed via the Monzo developer community. Its backing_loan sibling
// account returns 403 on every endpoint, so it's deliberately excluded.
const SYNCED_ACCOUNT_TYPES = new Set(["uk_retail", "uk_monzo_flex"]);

// Fetch every transaction for an account from `since` onward, following Monzo's
// cursor pagination. `since` may be an ISO timestamp or a transaction id. Returns
// the raw (unfiltered) transactions so callers decide how to filter. Throws with a
// `.status` on a non-2xx response.
async function fetchMonzoTransactions(
  accountId: string,
  accessToken: string,
  since: string,
): Promise<MonzoTx[]> {
  const all: MonzoTx[] = [];
  let cursor = since;
  for (let page = 0; page < 50; page++) {
    const url = new URL("https://api.monzo.com/transactions");
    url.searchParams.set("account_id", accountId);
    url.searchParams.set("since", cursor);
    url.searchParams.set("limit", "100");
    url.searchParams.set("expand[]", "merchant");

    const resp = await fetch(url.toString(), {
      headers: { Authorization: `Bearer ${accessToken}` },
    });
    if (!resp.ok) {
      throw Object.assign(new Error(await resp.text()), { status: resp.status });
    }

    const data = (await resp.json()) as { transactions: MonzoTx[] };
    all.push(...data.transactions);
    if (data.transactions.length < 100) break;
    cursor = data.transactions[data.transactions.length - 1].id;
  }
  return all;
}

// Shape of a single missing transaction recorded in a rec run's results.
type RecMissingTx = {
  id: string;
  created: string;
  amountPence: number;
  description: string;
};

// Per-account outcome of a rec pass, persisted as JSON on MonzoRecRun.results.
type RecAccountResult = {
  accountId: string;
  accountType: string;
  apiSettledCount: number;
  missingCount: number;
  missing: RecMissingTx[];
  error?: string;
};

// Independently pull the full last-90-day window (the most the Monzo API serves
// once a token is >5 min past authentication) and report any settled transaction
// present in the API but absent from our staging table. The incremental sync only
// fetches forward from each account's newest stored transaction, so a transaction
// an earlier sync missed would never be re-fetched — this pass is the safety net
// that surfaces such gaps. Every run is persisted (MonzoRecRun) and gaps are also
// reported to Sentry. Best-effort: never throws into the caller.
async function reconcileLast90Days(
  syncAccounts: { id: string; type: string }[],
  accessToken: string,
  trigger: "sync" | "manual",
) {
  const windowStart = new Date(Date.now() - 89 * 24 * 60 * 60 * 1000);
  const results: RecAccountResult[] = [];
  let totalMissing = 0;

  for (const account of syncAccounts) {
    let apiTxs: MonzoTx[];
    try {
      apiTxs = await fetchMonzoTransactions(account.id, accessToken, windowStart.toISOString());
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      Sentry.captureMessage(
        `Monzo 90-day rec fetch failed for ${account.type} (${account.id}): ${message}`,
        "warning",
      );
      results.push({
        accountId: account.id,
        accountType: account.type,
        apiSettledCount: 0,
        missingCount: 0,
        missing: [],
        error: message,
      });
      continue;
    }

    const settled = apiTxs.filter((tx) => tx.settled && !tx.decline_reason);
    const present = await db.monzoApiTransaction.findMany({
      where: { monzoId: { in: settled.map((tx) => tx.id) } },
      select: { monzoId: true },
    });
    const presentSet = new Set(present.map((r) => r.monzoId));
    const missing: RecMissingTx[] = settled
      .filter((tx) => !presentSet.has(tx.id))
      .map((tx) => ({
        id: tx.id,
        created: new Date(tx.created).toISOString(),
        amountPence: tx.amount,
        description: tx.merchant?.name ?? tx.description,
      }));

    totalMissing += missing.length;
    results.push({
      accountId: account.id,
      accountType: account.type,
      apiSettledCount: settled.length,
      missingCount: missing.length,
      missing,
    });

    if (missing.length > 0) {
      Sentry.withScope((scope) => {
        scope.setTag("monzo_account_type", account.type);
        scope.setContext("monzo_rec", {
          accountId: account.id,
          windowStart: windowStart.toISOString(),
          missingCount: missing.length,
          missing,
        });
        Sentry.captureMessage(
          `Monzo 90-day rec: ${missing.length} settled transaction(s) in the API missing from staging for ${account.type} (${account.id})`,
          "error",
        );
      });
    }
  }

  return db.monzoRecRun.create({
    data: { window: "90d", trigger, totalMissing, results: JSON.stringify(results) },
  });
}

// Resolve a fresh credential and the set of accounts we sync/reconcile against.
// Shared by /sync and /rec/run. Returns a discriminated result so the caller owns
// the HTTP response. `ensureFreshToken` may still throw 401 (dead refresh token) —
// that propagates to the Express error handler, same as before.
async function resolveMonzoSync(): Promise<
  | { ok: true; credential: MonzoCredential; syncAccounts: { id: string; type: string }[] }
  | { ok: false; status: number; error: string }
> {
  let credential = await db.monzoCredential.findFirst();
  if (!credential) {
    return { ok: false, status: 400, error: "Monzo not connected — click Connect Monzo first" };
  }

  credential = await ensureFreshToken(credential);

  const accountsResp = await fetch("https://api.monzo.com/accounts", {
    headers: { Authorization: `Bearer ${credential.accessToken}` },
  });
  if (!accountsResp.ok) {
    return {
      ok: false,
      status: 502,
      error: `Could not fetch Monzo accounts: ${await accountsResp.text()}`,
    };
  }
  const { accounts } = (await accountsResp.json()) as {
    accounts: { id: string; type: string; closed: boolean }[];
  };

  const syncAccounts = accounts.filter((a) => !a.closed && SYNCED_ACCOUNT_TYPES.has(a.type));
  if (syncAccounts.length === 0) {
    return { ok: false, status: 502, error: "No open Monzo retail or Flex account found" };
  }

  // Resolve primary (retail) account ID if not yet stored — used only for status display.
  if (!credential.accountId) {
    const retail = syncAccounts.find((a) => a.type === "uk_retail") ?? syncAccounts[0];
    credential = await db.monzoCredential.update({
      where: { id: credential.id },
      data: { accountId: retail.id },
    });
  }

  return { ok: true, credential, syncAccounts };
}

monzoRouter.post("/sync", requireAuth, async (_req, res) => {
  if (!monzoGuard(res)) return;

  const ctx = await resolveMonzoSync();
  if (!ctx.ok) {
    res.status(ctx.status).json({ error: ctx.error });
    return;
  }
  const { credential, syncAccounts } = ctx;

  let totalImported = 0;
  let totalDuplicates = 0;

  for (const account of syncAccounts) {
    // "since" must be a timestamp or a transaction ID belonging to *this* account,
    // so the cursor is tracked per account rather than globally.
    const latest = await db.monzoApiTransaction.findFirst({
      where: { accountId: account.id },
      orderBy: { created: "desc" },
      select: { monzoId: true },
    });
    const cursor = latest?.monzoId ?? "2025-12-31T23:59:59.000Z";

    let rawTxs: MonzoTx[];
    try {
      rawTxs = await fetchMonzoTransactions(account.id, credential.accessToken, cursor);
    } catch (err) {
      const body = err instanceof Error ? err.message : String(err);
      res.status(502).json({ error: `Monzo /transactions failed for ${account.type}: ${body}` });
      return;
    }

    const allTxs = rawTxs.filter((tx) => tx.settled && !tx.decline_reason);
    if (allTxs.length === 0) continue;

    const ids = allTxs.map((tx) => tx.id);
    const existing = await db.monzoApiTransaction.findMany({
      where: { monzoId: { in: ids } },
      select: { monzoId: true },
    });
    const existingSet = new Set(existing.map((r) => r.monzoId));

    const toInsert: Prisma.MonzoApiTransactionCreateManyInput[] = allTxs
      .filter((tx) => !existingSet.has(tx.id))
      .map((tx) => ({
        monzoId: tx.id,
        created: new Date(tx.created),
        settled: tx.settled ? new Date(tx.settled) : null,
        amountPence: tx.amount,
        currency: tx.currency,
        localAmountPence: tx.local_amount,
        localCurrency: tx.local_currency,
        description: tx.description,
        notes: tx.notes || null,
        monzoCategory: tx.category,
        merchantName: tx.merchant?.name ?? null,
        merchantEmoji: tx.merchant?.emoji ?? null,
        merchantAddress: tx.merchant?.address?.short_formatted ?? null,
        scheme: tx.scheme ?? null,
        includeInSpending: tx.include_in_spending,
        accountId: account.id,
      }));

    if (toInsert.length > 0) await db.monzoApiTransaction.createMany({ data: toInsert });

    totalImported += toInsert.length;
    totalDuplicates += allTxs.length - toInsert.length;
  }

  // Safety-net rec against the full 90-day window — persists a MonzoRecRun and
  // logs gaps to Sentry, without blocking the sync response.
  const recRun = await reconcileLast90Days(syncAccounts, credential.accessToken, "sync");

  res.json({
    imported: totalImported,
    duplicates: totalDuplicates,
    reconciled: { window: "90d", missing: recRun.totalMissing },
  });
});

// ─── GET /api/admin/monzo/rec — recent reconciliation runs ────────────────────

monzoRouter.get("/rec", requireAuth, async (_req, res) => {
  const runs = await db.monzoRecRun.findMany({ orderBy: { ranAt: "desc" }, take: 20 });
  res.json(runs.map((run) => ({ ...run, results: JSON.parse(run.results) })));
});

// ─── POST /api/admin/monzo/rec/run — run a reconciliation now ─────────────────

monzoRouter.post("/rec/run", requireAuth, async (_req, res) => {
  if (!monzoGuard(res)) return;

  const ctx = await resolveMonzoSync();
  if (!ctx.ok) {
    res.status(ctx.status).json({ error: ctx.error });
    return;
  }

  const run = await reconcileLast90Days(ctx.syncAccounts, ctx.credential.accessToken, "manual");
  res.json({ ...run, results: JSON.parse(run.results) });
});

// ─── POST /api/admin/monzo/disconnect ────────────────────────────────────────

monzoRouter.post("/disconnect", requireAuth, async (req, res) => {
  await db.monzoCredential.deleteMany({ where: { userId: req.user!.id } });
  res.json({ ok: true });
});

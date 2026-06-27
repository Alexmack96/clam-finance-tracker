import { Router } from "express";
import { randomBytes } from "crypto";
import { PlaidApi, PlaidEnvironments, Configuration, Products, CountryCode } from "plaid";
import { db } from "../db/client.js";
import { env } from "../config/env.js";
import { requireAuth } from "../middleware/auth.js";
import type { Prisma } from "../generated/prisma/index.js";

export const plaidRouter = Router();

// ─── Plaid client ─────────────────────────────────────────────────────────────

function makePlaidClient() {
  const config = new Configuration({
    basePath: PlaidEnvironments[env.PLAID_ENV ?? "sandbox"],
    baseOptions: {
      headers: {
        "PLAID-CLIENT-ID": env.PLAID_CLIENT_ID!,
        "PLAID-SECRET": env.PLAID_SECRET!,
      },
    },
  });
  return new PlaidApi(config);
}

function plaidGuard(res: import("express").Response): boolean {
  if (!env.PLAID_CLIENT_ID || !env.PLAID_SECRET) {
    res
      .status(503)
      .json({ error: "Plaid not configured — set PLAID_CLIENT_ID and PLAID_SECRET in .env" });
    return false;
  }
  return true;
}

// ─── GET /api/admin/plaid/status ─────────────────────────────────────────────

plaidRouter.get("/status", requireAuth, async (req, res) => {
  const configured = !!(env.PLAID_CLIENT_ID && env.PLAID_SECRET);
  const item = await db.plaidItem.findUnique({
    where: { userId: req.user!.id },
    select: { itemId: true },
  });
  const latest = await db.plaidTransaction.findFirst({
    orderBy: { date: "desc" },
    select: { date: true },
  });
  const totalStaged = await db.plaidTransaction.count();

  res.json({
    configured,
    env: env.PLAID_ENV ?? "sandbox",
    connected: !!item,
    lastSyncedAt: latest?.date ?? null,
    totalStaged,
  });
});

// ─── POST /api/admin/plaid/link-token — client calls this to get a link token ─

plaidRouter.post("/link-token", requireAuth, async (req, res) => {
  if (!plaidGuard(res)) return;

  const client = makePlaidClient();
  const response = await client.linkTokenCreate({
    user: { client_user_id: req.user!.id },
    client_name: "Clam Finance Tracker",
    products: [Products.Transactions],
    country_codes: [CountryCode.Gb],
    language: "en",
  });

  res.json({ link_token: response.data.link_token });
});

// ─── POST /api/admin/plaid/exchange — exchange public_token for access_token ──

plaidRouter.post("/exchange", requireAuth, async (req, res) => {
  if (!plaidGuard(res)) return;

  const { public_token } = req.body as { public_token: string };
  if (!public_token) {
    res.status(400).json({ error: "public_token required" });
    return;
  }

  const client = makePlaidClient();
  const response = await client.itemPublicTokenExchange({ public_token });
  const { access_token, item_id } = response.data;

  await db.plaidItem.upsert({
    where: { userId: req.user!.id },
    create: {
      id: randomBytes(8).toString("hex"),
      userId: req.user!.id,
      accessToken: access_token,
      itemId: item_id,
      updatedAt: new Date(),
    },
    update: { accessToken: access_token, itemId: item_id, cursor: null, updatedAt: new Date() },
  });

  console.log("[plaid:exchange] item saved:", item_id);
  res.json({ ok: true });
});

// ─── POST /api/admin/plaid/sync ───────────────────────────────────────────────

type PlaidTx = {
  transaction_id: string;
  date: string;
  name: string;
  merchant_name?: string | null;
  amount: number; // positive = expense, negative = income
  iso_currency_code: string | null;
  pending: boolean;
};

plaidRouter.post("/sync", requireAuth, async (req, res) => {
  if (!plaidGuard(res)) return;

  const item = await db.plaidItem.findUnique({ where: { userId: req.user!.id } });
  if (!item) {
    res.status(400).json({ error: "Santander not connected — link your account first" });
    return;
  }

  const client = makePlaidClient();
  let cursor = item.cursor ?? undefined;
  const added: PlaidTx[] = [];
  let hasMore = true;

  while (hasMore) {
    const response = await client.transactionsSync({
      access_token: item.accessToken,
      cursor,
      count: 500,
    });
    const data = response.data;
    added.push(...(data.added as PlaidTx[]).filter((tx) => !tx.pending));
    cursor = data.next_cursor;
    hasMore = data.has_more;
  }

  // Save cursor
  await db.plaidItem.update({ where: { id: item.id }, data: { cursor, updatedAt: new Date() } });

  if (added.length === 0) {
    res.json({ imported: 0, duplicates: 0 });
    return;
  }

  const ids = added.map((tx) => tx.transaction_id);
  const existing = await db.plaidTransaction.findMany({
    where: { transactionId: { in: ids } },
    select: { transactionId: true },
  });
  const existingSet = new Set(existing.map((r) => r.transactionId));

  const toInsert: Prisma.PlaidTransactionCreateManyInput[] = added
    .filter((tx) => !existingSet.has(tx.transaction_id))
    .map((tx) => ({
      id: randomBytes(8).toString("hex"),
      transactionId: tx.transaction_id,
      date: tx.date,
      description: tx.merchant_name ?? tx.name,
      amount: tx.amount,
      currency: tx.iso_currency_code ?? "GBP",
      merchantName: tx.merchant_name ?? null,
      owner: "Alex",
    }));

  if (toInsert.length > 0) await db.plaidTransaction.createMany({ data: toInsert });

  res.json({ imported: toInsert.length, duplicates: added.length - toInsert.length });
});

// ─── POST /api/admin/plaid/disconnect ────────────────────────────────────────

plaidRouter.post("/disconnect", requireAuth, async (req, res) => {
  const item = await db.plaidItem.findUnique({ where: { userId: req.user!.id } });
  if (item) {
    try {
      const client = makePlaidClient();
      await client.itemRemove({ access_token: item.accessToken });
    } catch {
      /* best-effort */
    }
    await db.plaidItem.delete({ where: { id: item.id } });
  }
  res.json({ ok: true });
});

import { db } from "../db/client.js";

// ─── System categories ───────────────────────────────────────────────────────

const SYSTEM_CATEGORIES: Record<string, { color: string; isFixed: boolean; isDirectDebit?: boolean }> = {
  "Activities":     { color: "#8b5cf6", isFixed: false },
  "Bank Sauce":     { color: "#0ea5e9", isFixed: false },
  "Entertainment":  { color: "#7C3AED", isFixed: false },
  "Food & Social":  { color: "#fb923c", isFixed: false },
  "Groceries":      { color: "#22c55e", isFixed: false },
  "Takeout":        { color: "#ef4444", isFixed: false },
  "Personal Care":  { color: "#f43f5e", isFixed: false },
  "Rent & Bills":   { color: "#64748b", isFixed: true  },
  "Savings":        { color: "#a855f7", isFixed: true  },
  "Transport":      { color: "#3b82f6", isFixed: true  },
  "Uncategorised":  { color: "#d1d5db", isFixed: false },
  "Vacation":       { color: "#eab308", isFixed: false },
};

export async function initSystemCategories() {
  for (const [name, { color, isFixed, isDirectDebit }] of Object.entries(SYSTEM_CATEGORIES)) {
    try {
      await db.category.upsert({
        where: { name },
        create: { name, color, isFixed, isDirectDebit: isDirectDebit ?? false },
        update: { isFixed, isDirectDebit: isDirectDebit ?? false },
      });
      console.log(`[initSystemCategories] upserted: ${name}`);
    } catch (err) {
      console.error(`[initSystemCategories] failed for ${name}:`, err);
    }
  }
}

export async function mapMonzoCategories() {
  const monzoMap: Record<string, string> = {
    "Eating out":    "Food & Social",
    "Entertainment": "Entertainment",
    "Golf":          "Activities",
    "Holidays":      "Vacation",
    "Income":        "Bank Sauce",
    "Personal care": "Personal Care",
    "Shopping":      "Uncategorised",
    "Transfers":     "Bank Sauce",
  };

  for (const [monzo, canonical] of Object.entries(monzoMap)) {
    const monzoCat = await db.category.findUnique({ where: { name: monzo } });
    if (!monzoCat) continue;
    const canonicalCat = await db.category.findUnique({ where: { name: canonical } });
    if (!canonicalCat) continue;

    await db.transaction.updateMany({
      where: { categoryId: monzoCat.id },
      data: { categoryId: canonicalCat.id },
    });
    const remaining = await db.transaction.count({ where: { categoryId: monzoCat.id } });
    if (remaining === 0) await db.category.delete({ where: { id: monzoCat.id } });
    console.log(`Mapped Monzo category: "${monzo}" → "${canonical}"`);
  }
}

export async function consolidateFoodCategories() {
  const target = await db.category.findUnique({ where: { name: "Food & Social" } });
  if (!target) return;

  for (const oldName of ["Eating Out", "Night Out"]) {
    const old = await db.category.findUnique({ where: { name: oldName } });
    if (!old) continue;
    const count = await db.transaction.updateMany({
      where: { categoryId: old.id },
      data: { categoryId: target.id },
    });
    await db.category.delete({ where: { id: old.id } });
    if (count.count > 0) console.log(`Consolidated ${count.count} "${oldName}" transactions → "Food & Social"`);
  }
}

export async function migrateTakeout() {
  const takeout = await db.category.findUnique({ where: { name: "Takeout" } });
  if (!takeout) return;

  const TAKEOUT_PATTERN = /deliveroo|uber.?eats|wingstop/i;
  const candidates = await db.transaction.findMany({
    where: { category: { name: "Food & Social" } },
    select: { id: true, description: true },
  });

  const toMove = candidates.filter((t) => TAKEOUT_PATTERN.test(t.description));
  if (toMove.length === 0) return;

  await db.transaction.updateMany({
    where: { id: { in: toMove.map((t) => t.id) } },
    data: { categoryId: takeout.id },
  });
  console.log(`Migrated ${toMove.length} takeout transactions → "Takeout"`);
}

export async function migrateOwners() {
  const alexIgnoreCat = await db.category.findUnique({ where: { name: "Alex Ignore" } });
  if (alexIgnoreCat) {
    await db.transaction.updateMany({
      where: { categoryId: alexIgnoreCat.id },
      data: { owner: "Alex" },
    });
  }

  const bankSauceCat = await db.category.findUnique({ where: { name: "Bank Sauce" } });

  if (bankSauceCat) {
    const unowned = await db.transaction.findMany({
      where: { categoryId: bankSauceCat.id, owner: "Joint" },
      select: { id: true, description: true },
    });
    for (const tx of unowned) {
      for (const p of [/mackintosh/i, /\balex\b/i]) {
        if (p.test(tx.description)) {
          await db.transaction.update({ where: { id: tx.id }, data: { owner: "Alex" } });
          break;
        }
      }
      for (const p of [/liddy/i, /\bcasey\b/i]) {
        if (p.test(tx.description)) {
          await db.transaction.update({ where: { id: tx.id }, data: { owner: "Casey" } });
          break;
        }
      }
    }
    if (unowned.length > 0) console.log(`Processed owner detection for ${unowned.length} Bank Sauce transactions`);
  }
}

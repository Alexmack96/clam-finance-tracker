import { db } from "../db/client.js";
import { Bucket } from "../generated/prisma/index.js";

// ─── System categories ───────────────────────────────────────────────────────

// The seed set, with the Bucket each one starts in. Uncategorised is deliberately
// left unmapped so un-ruled transactions stay uncategorised rather than being
// silently stamped as Wants.
const SYSTEM_CATEGORIES: Record<string, { color: string; bucket?: Bucket }> = {
  Activities: { color: "#8b5cf6", bucket: Bucket.Wants },
  "Bank Sauce": { color: "#0ea5e9", bucket: Bucket.Wants },
  Entertainment: { color: "#7C3AED", bucket: Bucket.Wants },
  "Food & Social": { color: "#fb923c", bucket: Bucket.Wants },
  Groceries: { color: "#22c55e", bucket: Bucket.Wants },
  Takeout: { color: "#ef4444", bucket: Bucket.Wants },
  "Personal Care": { color: "#f43f5e", bucket: Bucket.Wants },
  "Rent & Bills": { color: "#64748b", bucket: Bucket.Needs },
  Savings: { color: "#a855f7", bucket: Bucket.Savings },
  Transport: { color: "#3b82f6", bucket: Bucket.Needs },
  Uncategorised: { color: "#d1d5db" },
  Vacation: { color: "#eab308", bucket: Bucket.Wants },
};

// Upsert-only: creates the default set if missing, never deletes or renames a
// category a user has added. Existing categories are left untouched on update.
//
// The Bucket a category starts in is seeded as a real Bucket rule rather than a
// hidden column, so bucketing is decided in exactly one place. A rule is only
// created when the category itself is new — re-running this must never
// resurrect a rule the user deleted on purpose.
export async function initSystemCategories() {
  for (const [name, { color, bucket }] of Object.entries(SYSTEM_CATEGORIES)) {
    try {
      const existing = await db.category.findUnique({ where: { name }, select: { id: true } });
      if (existing) continue;

      await db.category.create({ data: { name, color } });
      if (!bucket) continue;

      const last = await db.rule.findFirst({
        where: { kind: "Bucket" },
        orderBy: { position: "desc" },
        select: { position: true },
      });
      await db.rule.create({
        data: {
          kind: "Bucket",
          position: (last?.position ?? -1) + 1,
          joinOperator: "AND",
          bucket,
          conditions: {
            create: [
              { field: "Category", operator: "Exact", value: name, negate: false, position: 0 },
            ],
          },
        },
      });
    } catch (err) {
      console.error(`[initSystemCategories] failed for ${name}:`, err);
    }
  }
}

import { db } from "../db/client.js";
import { Bucket } from "../generated/prisma/index.js";

// ─── System categories ───────────────────────────────────────────────────────

// Default Bucket mapping for the seed set. Uncategorised is deliberately left
// unmapped (null) so un-ruled expenses stay uncategorised rather than being
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
// category a user has added. Existing categories are left untouched on update —
// the user owns their Bucket mappings, so we never clobber them on boot.
export async function initSystemCategories() {
  for (const [name, { color, bucket }] of Object.entries(SYSTEM_CATEGORIES)) {
    try {
      await db.category.upsert({
        where: { name },
        create: { name, color, bucket },
        update: {},
      });
    } catch (err) {
      console.error(`[initSystemCategories] failed for ${name}:`, err);
    }
  }
}

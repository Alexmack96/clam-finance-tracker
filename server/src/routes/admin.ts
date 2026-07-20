import { db } from "../db/client.js";
import { SavingType } from "../generated/prisma/index.js";

// ─── System categories ───────────────────────────────────────────────────────

const SYSTEM_CATEGORIES: Record<string, { color: string; savingType: SavingType }> = {
  "Activities":     { color: "#8b5cf6", savingType: SavingType.Fun    },
  "Bank Sauce":     { color: "#0ea5e9", savingType: SavingType.Fun    },
  "Entertainment":  { color: "#7C3AED", savingType: SavingType.Fun    },
  "Food & Social":  { color: "#fb923c", savingType: SavingType.Fun    },
  "Groceries":      { color: "#22c55e", savingType: SavingType.Fun    },
  "Takeout":        { color: "#ef4444", savingType: SavingType.Fun    },
  "Personal Care":  { color: "#f43f5e", savingType: SavingType.Fun    },
  "Rent & Bills":   { color: "#64748b", savingType: SavingType.Fixed  },
  "Savings":        { color: "#a855f7", savingType: SavingType.Saving },
  "Transport":      { color: "#3b82f6", savingType: SavingType.Fixed  },
  "Uncategorised":  { color: "#d1d5db", savingType: SavingType.Fun    },
  "Vacation":       { color: "#eab308", savingType: SavingType.Fun    },
};

// Upsert-only: creates the default set if missing, never deletes or renames a
// category a user has added. Anything users add in prod (e.g. a new "Golf"
// category) is left alone — nothing here ever touches or removes it.
export async function initSystemCategories() {
  for (const [name, { color, savingType }] of Object.entries(SYSTEM_CATEGORIES)) {
    try {
      await db.category.upsert({
        where: { name },
        create: { name, color, savingType },
        update: { savingType },
      });
    } catch (err) {
      console.error(`[initSystemCategories] failed for ${name}:`, err);
    }
  }
}

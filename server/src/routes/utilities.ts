import { Router } from "express";
import { db } from "../db/client.js";

export const utilitiesRouter = Router();

const UTILITY_NAMES = ["Rent", "Water", "Wifi", "Electricity", "Council Tax"];

utilitiesRouter.get("/", async (req, res) => {
  const now = new Date();
  const monthStart = new Date(now.getFullYear(), now.getMonth(), 1);
  const monthEnd = new Date(now.getFullYear(), now.getMonth() + 1, 1);

  const categories = await db.category.findMany({
    where: { name: { in: UTILITY_NAMES } },
  });

  const utilities = await Promise.all(
    categories.map(async (cat) => {
      const transactions = await db.transaction.findMany({
        where: {
          categoryId: cat.id,
          date: { gte: monthStart, lt: monthEnd },
        },
        orderBy: { date: "asc" },
      });

      const payments: Record<string, { amount: string; date: string; description: string }[]> = {
        Alex: [],
        Casey: [],
        Joint: [],
      };

      let totalThisMonth = 0;
      for (const tx of transactions) {
        const entry = {
          amount: tx.amount.toString(),
          date: tx.date.toISOString(),
          description: tx.description,
        };
        payments[tx.owner].push(entry);
        totalThisMonth += Number(tx.amount);
      }

      return {
        id: cat.id,
        name: cat.name,
        color: cat.color,
        payments,
        totalThisMonth,
      };
    })
  );

  // Return in display order
  const ordered = UTILITY_NAMES.map((name) => utilities.find((u) => u.name === name)).filter(Boolean);

  const month = now.toLocaleString("en-GB", { month: "long", year: "numeric" });

  res.json({ month, utilities: ordered });
});

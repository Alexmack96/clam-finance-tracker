import { Router } from "express";
import { db } from "../db/client.js";
import { TransactionType } from "../generated/prisma/index.js";

export const dashboardRouter = Router();

dashboardRouter.get("/summary", async (_req, res) => {
  const all = await db.transaction.findMany({ include: { category: true } });

  let caseyIn = 0;
  let jointExpenses = 0;
  const categoryTotals: Record<string, { name: string; color: string; value: number }> = {};

  for (const t of all) {
    const amount = Number(t.amount);
    if (t.category.name === "Bank Sauce" && t.owner === "Casey" && t.externalId?.startsWith("monzo:")) {
      caseyIn += amount;
    } else if (t.owner === "Joint") {
      const signed = t.type === TransactionType.Expense ? amount : -amount;
      jointExpenses += signed;
      if (t.type === TransactionType.Expense) {
        const key = t.categoryId;
        if (!categoryTotals[key]) {
          categoryTotals[key] = { name: t.category.name, color: t.category.color, value: 0 };
        }
        categoryTotals[key].value += amount;
      }
    }
  }

  const settlement = caseyIn - jointExpenses / 2;

  res.json({
    caseyIn,
    jointExpenses,
    settlement,
    spendingByCategory: Object.values(categoryTotals),
  });
});

dashboardRouter.get("/analytics", async (_req, res) => {
  const now = new Date();
  const yearStart = new Date(now.getFullYear(), 0, 1);
  const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
  const currentMonthIdx = now.getMonth();

  const transactions = await db.transaction.findMany({
    where: { date: { gte: yearStart } },
    include: { category: true },
    orderBy: { date: "asc" },
  });

  const expenses = transactions.filter((t) => t.type === TransactionType.Expense);

  const fixedVsVariable = MONTHS.slice(0, currentMonthIdx + 1).map((month, i) => {
    const monthExp = expenses.filter((t) => new Date(t.date).getMonth() === i);
    return {
      month,
      fixed:    monthExp.filter((t) => t.category.isFixed).reduce((s, t) => s + Number(t.amount), 0),
      variable: monthExp.filter((t) => !t.category.isFixed).reduce((s, t) => s + Number(t.amount), 0),
    };
  });

  const FUN_CATEGORIES = ["Food & Social", "Activities"];
  const funCats = await db.category.findMany({ where: { name: { in: FUN_CATEGORIES } } });
  const funCatMap = Object.fromEntries(funCats.map((c) => [c.name, c]));

  const monthlyFun = MONTHS.slice(0, currentMonthIdx + 1).map((month, i) => {
    const monthExp = expenses.filter((t) => new Date(t.date).getMonth() === i);
    const entry: Record<string, number | string> = { month };
    for (const name of FUN_CATEGORIES) {
      const cat = funCatMap[name];
      entry[name] = cat
        ? monthExp.filter((t) => t.categoryId === cat.id).reduce((s, t) => s + Number(t.amount), 0)
        : 0;
    }
    return entry;
  });

  const catMap: Record<string, { name: string; color: string; value: number }> = {};
  for (const t of expenses) {
    if (!catMap[t.categoryId]) catMap[t.categoryId] = { name: t.category.name, color: t.category.color, value: 0 };
    catMap[t.categoryId].value += Number(t.amount);
  }
  const spendingByCategory = Object.values(catMap).sort((a, b) => b.value - a.value);

  const ownerMap: Record<string, number> = {};
  for (const t of expenses) ownerMap[t.owner] = (ownerMap[t.owner] ?? 0) + Number(t.amount);
  const ownerBreakdown = Object.entries(ownerMap)
    .map(([owner, amount]) => ({ owner, amount }))
    .filter((o) => o.amount > 0);

  const merchantMap: Record<string, number> = {};
  for (const t of expenses) merchantMap[t.description] = (merchantMap[t.description] ?? 0) + Number(t.amount);
  const topMerchants = Object.entries(merchantMap)
    .map(([name, amount]) => ({ name, amount }))
    .sort((a, b) => b.amount - a.amount)
    .slice(0, 10);

  const funCategories = FUN_CATEGORIES.map((name) => ({ name, color: funCatMap[name]?.color ?? "#888" }));

  const vacationCat = await db.category.findUnique({ where: { name: "Vacation" } });
  const monthlyVacation = MONTHS.slice(0, currentMonthIdx + 1).map((month, i) => ({
    month,
    amount: vacationCat
      ? expenses.filter((t) => t.categoryId === vacationCat.id && new Date(t.date).getMonth() === i).reduce((s, t) => s + Number(t.amount), 0)
      : 0,
  }));
  const vacationColor = vacationCat?.color ?? "#eab308";

  const FOOD_CATEGORIES = ["Food & Social", "Groceries", "Takeout"];
  const foodCats = await db.category.findMany({ where: { name: { in: FOOD_CATEGORIES } } });
  const foodCatMap = Object.fromEntries(foodCats.map((c) => [c.name, c]));

  const monthlyFood = MONTHS.slice(0, currentMonthIdx + 1).map((month, i) => {
    const monthExp = expenses.filter((t) => new Date(t.date).getMonth() === i);
    const entry: Record<string, number | string> = { month };
    for (const name of FOOD_CATEGORIES) {
      const cat = foodCatMap[name];
      entry[name] = cat
        ? monthExp.filter((t) => t.categoryId === cat.id).reduce((s, t) => s + Number(t.amount), 0)
        : 0;
    }
    return entry;
  });
  const foodCategories = FOOD_CATEGORIES.map((name) => ({ name, color: foodCatMap[name]?.color ?? "#888" }));

  res.json({ fixedVsVariable, monthlyFun, funCategories, monthlyVacation, vacationColor, monthlyFood, foodCategories, spendingByCategory, ownerBreakdown, topMerchants });
});

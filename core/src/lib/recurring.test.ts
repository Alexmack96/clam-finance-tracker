import { describe, expect, test } from "bun:test";
import { detectRecurring, monthlyEquivalent, type RecurringCandidate } from "./recurring.js";

/** Build a series of charges `gapDays` apart, with optional per-occurrence jitter. */
function series(
  description: string,
  start: string,
  gapDays: number,
  count: number,
  extra: Partial<RecurringCandidate> & { jitter?: number[]; amounts?: number[] } = {},
): RecurringCandidate[] {
  const { jitter = [], amounts = [], ...rest } = extra;
  const out: RecurringCandidate[] = [];
  let day = new Date(start).getTime();
  for (let i = 0; i < count; i++) {
    if (i > 0) day += (gapDays + (jitter[i - 1] ?? 0)) * 86_400_000;
    out.push({
      description,
      date: new Date(day).toISOString(),
      amount: amounts[i] ?? 9.99,
      type: "Expense",
      bank: "amex",
      bucket: "Wants",
      categoryName: "Entertainment",
      ...rest,
    });
  }
  return out;
}

describe("cadence bands", () => {
  test("a rigid monthly charge is detected", () => {
    const [s] = detectRecurring(series("NETFLIX", "2026-01-05", 30, 6));
    expect(s?.cadence).toBe("Monthly");
    expect(s?.occurrences).toBe(6);
    expect(s?.active).toBe(true);
  });

  test("weekly, quarterly and yearly bands all resolve", () => {
    expect(detectRecurring(series("W", "2026-01-01", 7, 20))[0]?.cadence).toBe("Weekly");
    expect(detectRecurring(series("Q", "2024-01-01", 91, 6))[0]?.cadence).toBe("Quarterly");
    expect(detectRecurring(series("Y", "2020-01-01", 365, 5))[0]?.cadence).toBe("Yearly");
  });

  test("a gap that matches no band is not recurring", () => {
    // 57 days — the real Trading 212 withdrawals. Between monthly and quarterly.
    expect(detectRecurring(series("T212", "2026-01-01", 57, 4))).toHaveLength(0);
  });
});

describe("separating subscriptions from habits", () => {
  test("timing scatter rejects a habit that averages a real cadence", () => {
    // Median gap is 14 days, but the visits are 2, 26, 14, 14 days apart —
    // exactly how a coffee habit looks. A fortnightly subscription would not.
    const habit = series("PRET A MANGER", "2026-01-01", 14, 5, { jitter: [-12, 12, 0, 0] });
    expect(detectRecurring(habit)).toHaveLength(0);
  });

  test("a wildly variable amount is still recurring if the timing is rigid", () => {
    // Energy bills swing by 3x across a year. Amount is deliberately not a signal.
    const bill = series("SO ENERGY", "2026-01-01", 30, 6, {
      amounts: [140.11, 132.4, 96.2, 61.05, 48.35, 51.11],
      bucket: "Needs",
    });
    const [s] = detectRecurring(bill);
    expect(s?.cadence).toBe("Monthly");
    expect(s?.averageAmount).toBeCloseTo(88.2, 1);
  });

  test("fewer than three occurrences is never enough", () => {
    // Two points make any interval look perfect.
    expect(detectRecurring(series("TWICE", "2026-01-01", 30, 2))).toHaveLength(0);
  });
});

describe("coverage", () => {
  test("a short cadence seen only a few times over a long window is rejected", () => {
    // Claims an 8-day cycle but appears 3 times across ~7 months, alongside a
    // long-running series that stretches the window. ~26 were predicted.
    const fluke = series("MARKET HALL", "2026-01-01", 8, 3);
    const anchor = series("ANCHOR", "2026-01-01", 30, 8);
    const found = detectRecurring([...fluke, ...anchor]).map((s) => s.description);
    expect(found).toContain("ANCHOR");
    expect(found).not.toContain("MARKET HALL");
  });
});

describe("same-day repeat charges", () => {
  test("a burst of same-day charges is not recurring", () => {
    // Median gap 0 makes deviation/median NaN, and `NaN > threshold` is false —
    // without an explicit guard this passes every other test.
    const sameDay: RecurringCandidate[] = Array.from({ length: 6 }, () => ({
      description: "BRITISH AIRWAYS",
      date: "2026-03-01T00:00:00.000Z",
      amount: 210,
      type: "Expense" as const,
      bank: "amex",
      bucket: "Wants" as const,
      categoryName: "Vacation",
    }));
    expect(detectRecurring(sameDay)).toHaveLength(0);
  });
});

describe("active, judged per bank", () => {
  test("a stale bank does not make its subscriptions look cancelled", () => {
    // Barclays' last statement is months before Monzo's. The Barclays series is
    // current *for Barclays*, and must not be reported as stopped.
    const barclays = series("IONOS", "2026-01-10", 30, 4, { bank: "barclays" });
    const monzo = series("SPOTIFY", "2026-01-10", 30, 7, { bank: "monzo" });
    const found = detectRecurring([...barclays, ...monzo]);
    const ionos = found.find((s) => s.description === "IONOS");
    expect(ionos?.active).toBe(true);
    expect(ionos?.bankDataEndsAt).not.toBeNull();
    expect(found.find((s) => s.description === "SPOTIFY")?.bankDataEndsAt).toBeNull();
  });

  test("a recently stopped series on a current bank shows as inactive", () => {
    // Two cycles of silence: still detected, flagged as stopped, so a
    // cancellation is visible rather than silently vanishing.
    const stopped = series("O2", "2026-01-01", 30, 6, { bank: "monzo" });
    const current = series("RENT", "2026-01-01", 30, 8, { bank: "monzo" });
    const found = detectRecurring([...stopped, ...current]);
    expect(found.find((s) => s.description === "O2")?.active).toBe(false);
    expect(found.find((s) => s.description === "RENT")?.active).toBe(true);
  });

  test("a long-abandoned series drops out of the list entirely", () => {
    // Coverage is measured to the data horizon, not to the series' own last
    // charge, so the longer something has been dead the worse it scores until
    // it disappears. That is deliberate — a list of live commitments should not
    // accumulate everything ever cancelled.
    const abandoned = series("OLD GYM", "2026-01-01", 30, 4, { bank: "monzo" });
    const current = series("RENT", "2026-01-01", 30, 12, { bank: "monzo" });
    const found = detectRecurring([...abandoned, ...current]).map((s) => s.description);
    expect(found).toEqual(["RENT"]);
  });
});

describe("derived fields", () => {
  test("next due is projected one cadence past the last charge", () => {
    const [s] = detectRecurring(series("SKY", "2026-01-01", 30, 5));
    // 5 charges 30 days apart from 1 Jan → last is 1 May, next projected 31 May.
    expect(s?.nextDueDate.slice(0, 10)).toBe("2026-05-31");
  });

  test("income and expense are distinguished", () => {
    const [s] = detectRecurring(
      series("BLOOM LP WAGES", "2026-01-01", 30, 5, { type: "Income", bucket: null }),
    );
    expect(s?.kind).toBe("Income");
  });

  test("monthlyEquivalent normalises cadences so they can be totalled", () => {
    const [weekly] = detectRecurring(
      series("W", "2026-01-01", 7, 20, { amounts: Array(20).fill(10) }),
    );
    const [yearly] = detectRecurring(
      series("Y", "2020-01-01", 365, 5, { amounts: Array(5).fill(120) }),
    );
    expect(monthlyEquivalent(weekly!)).toBeCloseTo(43.5, 0);
    expect(monthlyEquivalent(yearly!)).toBeCloseTo(10, 0);
  });
});

import type { Bucket } from "../schemas/rules.js";
import type { RecurringCadence, RecurringKind } from "../schemas/recurring.js";

/**
 * Recurring-payment detection.
 *
 * Lives in `core` for the same reason the rule engine does: the page, the
 * (future) grid filter and any forecast built on top must never disagree about
 * what counts as recurring.
 *
 * The load-bearing insight is that **timing, not amount, is the signal**. A
 * subscription and a fortnightly coffee habit are indistinguishable by interval
 * *length*, but not by interval *consistency* — real recurring items land within
 * ~10% of their cadence, habits scatter by 150–1200%. Amount stability is
 * deliberately unused: variable bills (energy, council tax) swing wildly in
 * price while keeping rigid timing, and gating on amount would drop them.
 */

/** The minimum a transaction needs to expose to be considered. */
export type RecurringCandidate = {
  description: string;
  /** ISO date string. */
  date: string;
  /** Always positive; direction comes from `type`. */
  amount: string | number;
  type: "Income" | "Expense";
  /** Namespace from `externalId` (`monzo`, `amex`, …), or null. */
  bank: string | null;
  bucket: Bucket | null;
  categoryName: string;
};

export type RecurringSeries = {
  /** Stable identity: exact description is *why* these rows grouped at all. */
  description: string;
  kind: RecurringKind;
  cadence: RecurringCadence;
  /** Median days between occurrences. */
  medianGapDays: number;
  occurrences: number;
  /** Worst gap's deviation from the median, as a fraction of it. Lower is tighter. */
  irregularity: number;
  /** Seen ÷ predicted-by-cadence, over the window observed. */
  coverage: number;
  lastDate: string;
  lastAmount: number;
  /** Mean of every occurrence — the honest figure for a variable bill. */
  averageAmount: number;
  /** Projected from `lastDate + medianGapDays`. */
  nextDueDate: string;
  bucket: Bucket | null;
  categoryName: string;
  bank: string | null;
  /** Still running, judged against the bank's own data horizon (see below). */
  active: boolean;
  daysSinceLast: number;
  /**
   * Set when this series' bank has no data since `bankDataEndsAt` — `active` is
   * then a statement about stale data, not about the subscription.
   */
  bankDataEndsAt: string | null;
};

const DAY_MS = 86_400_000;

/** Named so a reader can see why a fortnightly coffee run doesn't qualify. */
const MIN_OCCURRENCES = 3;
/** Real items measure 3–18%; the nearest habit is 150%. */
const MAX_IRREGULARITY = 0.25;
/** Real items measure 80–100%; short-cadence flukes measure 23–27%. */
const MIN_COVERAGE = 0.6;
/** One missed cycle is a blip; half a cycle beyond that is a cancellation. */
const ACTIVE_CYCLE_TOLERANCE = 1.5;

const BANDS: { cadence: RecurringCadence; min: number; max: number }[] = [
  { cadence: "Weekly", min: 5, max: 9 },
  { cadence: "Fortnightly", min: 12, max: 16 },
  { cadence: "Monthly", min: 26, max: 35 },
  { cadence: "Quarterly", min: 85, max: 100 },
  { cadence: "Yearly", min: 350, max: 380 },
];

function median(values: number[]): number {
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.floor(sorted.length / 2)] ?? 0;
}

function toNumber(amount: string | number): number {
  return typeof amount === "string" ? parseFloat(amount) : amount;
}

/**
 * Latest transaction date per bank.
 *
 * "Is it still running?" has to be asked against the data we actually hold. A
 * bank whose last statement was three months ago would otherwise report every
 * one of its subscriptions as cancelled.
 */
export function bankHorizons(txns: RecurringCandidate[]): Record<string, number> {
  const horizons: Record<string, number> = {};
  for (const t of txns) {
    if (!t.bank) continue;
    const time = new Date(t.date).getTime();
    if (!Number.isFinite(time)) continue;
    horizons[t.bank] = Math.max(horizons[t.bank] ?? 0, time);
  }
  return horizons;
}

/**
 * Group by exact description and keep the groups that recur on a tight schedule.
 *
 * Exact matching is deliberate: normalising bank references away was measured
 * against this dataset and recovered *zero* additional series. The noisy-reference
 * descriptions are ad-hoc card payments, which fail the timing test whether or
 * not they group.
 */
export function detectRecurring(txns: RecurringCandidate[]): RecurringSeries[] {
  const horizons = bankHorizons(txns);
  const globalHorizon = Math.max(0, ...Object.values(horizons));

  const groups = new Map<string, RecurringCandidate[]>();
  for (const t of txns) {
    const list = groups.get(t.description);
    if (list) list.push(t);
    else groups.set(t.description, [t]);
  }

  const series: RecurringSeries[] = [];

  for (const [description, rows] of groups) {
    if (rows.length < MIN_OCCURRENCES) continue;

    const sorted = [...rows].sort(
      (a, b) => new Date(a.date).getTime() - new Date(b.date).getTime(),
    );
    const times = sorted.map((r) => new Date(r.date).getTime());
    if (times.some((t) => !Number.isFinite(t))) continue;

    const gaps: number[] = [];
    for (let i = 1; i < times.length; i++) gaps.push((times[i]! - times[i - 1]!) / DAY_MS);

    const medianGap = median(gaps);
    // Several same-day charges give a median of 0, and `NaN > threshold` is
    // false — without this guard every burst of same-day repeats passes every
    // test below.
    if (!Number.isFinite(medianGap) || medianGap <= 0) continue;

    const band = BANDS.find((b) => medianGap >= b.min && medianGap <= b.max);
    if (!band) continue;

    const irregularity = Math.max(...gaps.map((g) => Math.abs(g - medianGap))) / medianGap;
    if (irregularity > MAX_IRREGULARITY) continue;

    const last = sorted[sorted.length - 1]!;
    const bank = last.bank;
    const horizon =
      (bank ? horizons[bank] : undefined) ?? globalHorizon ?? times[times.length - 1]!;

    // How many occurrences the cadence predicts across the window we observed.
    const windowDays = (horizon - times[0]!) / DAY_MS;
    const predicted = Math.max(Math.floor(windowDays / medianGap) + 1, 1);
    const coverage = rows.length / predicted;
    if (coverage < MIN_COVERAGE) continue;

    const daysSinceLast = (horizon - times[times.length - 1]!) / DAY_MS;
    const amounts = sorted.map((r) => toNumber(r.amount));

    series.push({
      description,
      kind: last.type === "Income" ? "Income" : "Expense",
      cadence: band.cadence,
      medianGapDays: Math.round(medianGap),
      occurrences: rows.length,
      irregularity,
      coverage: Math.min(coverage, 1),
      lastDate: new Date(times[times.length - 1]!).toISOString(),
      lastAmount: amounts[amounts.length - 1]!,
      averageAmount: amounts.reduce((s, a) => s + a, 0) / amounts.length,
      nextDueDate: new Date(times[times.length - 1]! + medianGap * DAY_MS).toISOString(),
      bucket: last.bucket,
      categoryName: last.categoryName,
      bank,
      active: daysSinceLast <= medianGap * ACTIVE_CYCLE_TOLERANCE,
      daysSinceLast: Math.round(daysSinceLast),
      // Only worth surfacing when this bank lags the freshest data we hold —
      // otherwise every series on the newest bank would carry a pointless badge.
      bankDataEndsAt:
        bank && horizons[bank] !== undefined && horizons[bank]! < globalHorizon
          ? new Date(horizons[bank]!).toISOString()
          : null,
    });
  }

  return series.sort((a, b) => b.lastAmount - a.lastAmount);
}

/**
 * What a cadence costs per month, so weekly and yearly items can be totalled
 * against each other. Uses the average rather than the last amount — a variable
 * bill's most recent charge is not its typical one.
 */
export function monthlyEquivalent(s: RecurringSeries): number {
  return (s.averageAmount * 365.25) / (s.medianGapDays * 12);
}

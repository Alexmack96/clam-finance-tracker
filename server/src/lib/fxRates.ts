// Historical FX lookup via Frankfurter (free, no API key, ECB data).
// Frankfurter auto-falls back to the nearest previous weekday when a requested
// date is a weekend/holiday, so we don't need to handle that here.

import * as Sentry from "@sentry/node";

interface FrankfurterResponse {
  amount: number;
  base: string;
  date: string;
  rates: Record<string, number>;
}

// Last-resort rates if both historical AND latest lookups fail. Keep these
// approximate — they exist to prevent data loss, not to be accurate. Any row
// that ends up using one is reported to Sentry with its external id so the
// rate can be corrected manually later.
const HARDCODED_FALLBACKS: Record<string, number> = {
  "USD:GBP": 0.79,
};

const cache = new Map<string, number>();

function isoDate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function round2(n: number): number {
  return Math.round(n * 100) / 100;
}

function errMsg(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

export async function getRate(from: string, to: string, date: Date): Promise<number> {
  const key = `${from}:${to}:${isoDate(date)}`;
  const cached = cache.get(key);
  if (cached !== undefined) return cached;

  const url = `https://api.frankfurter.dev/v1/${isoDate(date)}?base=${from}&symbols=${to}`;
  const res = await fetch(url);
  if (!res.ok) throw new Error(`Frankfurter ${res.status} for ${url}`);
  const data = (await res.json()) as FrankfurterResponse;
  const rate = data.rates?.[to];
  if (typeof rate !== "number") throw new Error(`Missing ${to} rate in Frankfurter response for ${isoDate(date)}`);
  cache.set(key, rate);
  return rate;
}

export async function getLatestRate(from: string, to: string): Promise<number> {
  const key = `${from}:${to}:latest`;
  const cached = cache.get(key);
  if (cached !== undefined) return cached;

  const url = `https://api.frankfurter.dev/v1/latest?base=${from}&symbols=${to}`;
  const res = await fetch(url);
  if (!res.ok) throw new Error(`Frankfurter latest ${res.status} for ${url}`);
  const data = (await res.json()) as FrankfurterResponse;
  const rate = data.rates?.[to];
  if (typeof rate !== "number") throw new Error(`Missing ${to} rate in Frankfurter latest response`);
  cache.set(key, rate);
  return rate;
}

export async function convert(amount: number, from: string, to: string, date: Date): Promise<number> {
  if (from === to) return amount;
  const rate = await getRate(from, to, date);
  return round2(amount * rate);
}

export interface ConvertResult {
  amount: number;
  rate: number;
  /** null on the happy path; otherwise the kind of fallback used. */
  fallback: "latest" | "hardcoded" | null;
}

/**
 * Like {@link convert}, but never throws. Falls back to the latest rate, then
 * to a hardcoded constant, rather than letting a Frankfurter outage block the
 * transaction. Every fallback is reported to Sentry tagged with `contextId`
 * (typically the row's external id) so the rate can be fixed up later.
 */
export async function convertWithFallback(
  amount: number,
  from: string,
  to: string,
  date: Date,
  contextId: string,
): Promise<ConvertResult> {
  if (from === to) return { amount, rate: 1, fallback: null };

  try {
    const rate = await getRate(from, to, date);
    return { amount: round2(amount * rate), rate, fallback: null };
  } catch (err) {
    Sentry.captureMessage(
      `FX historical rate failed for ${contextId} (${from}→${to} on ${isoDate(date)}): ${errMsg(err)}`,
      "warning",
    );
  }

  try {
    const rate = await getLatestRate(from, to);
    Sentry.captureMessage(
      `FX using latest rate ${rate} as fallback for ${contextId} (${from}→${to}, requested ${isoDate(date)})`,
      "warning",
    );
    return { amount: round2(amount * rate), rate, fallback: "latest" };
  } catch (err) {
    Sentry.captureMessage(
      `FX latest rate also failed for ${contextId} (${from}→${to}): ${errMsg(err)}`,
      "error",
    );
  }

  const rate = HARDCODED_FALLBACKS[`${from}:${to}`] ?? 1;
  Sentry.captureMessage(
    `FX using hardcoded fallback ${rate} for ${contextId} (${from}→${to} on ${isoDate(date)}) — fix manually`,
    "error",
  );
  return { amount: round2(amount * rate), rate, fallback: "hardcoded" };
}

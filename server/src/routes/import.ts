import { createHash } from "crypto";
import { Router } from "express";
import multer from "multer";
import pdfParse from "pdf-parse";
import { db } from "../db/client.js";
import { convertWithFallback } from "../lib/fxRates.js";
import { statementSchemas } from "../lib/statementValidation.js";
import { resolveRuleCategory } from "../lib/categoryRules.js";

export const importRouter = Router();

const upload = multer({
  storage: multer.memoryStorage(),
  limits: { fileSize: 20 * 1024 * 1024 },
});

// ─── Monzo API category slug → system category ──────────────────────────────

const MONZO_API_CATEGORY_MAP: Record<string, string> = {
  eating_out: "Food & Social",
  entertainment: "Entertainment",
  groceries: "Groceries",
  holidays: "Vacation",
  income: "Net",
  personal_care: "Personal Care",
  savings: "Investments",
  shopping: "Uncategorised",
  transfers: "Net",
  transport: "Transport",
  bills: "Uncategorised",
  finances: "Net",
  general: "Uncategorised",
};

// ─── Merchant name overrides ─────────────────────────────────────────────────

const MERCHANT_OVERRIDES: { pattern: RegExp; category: string }[] = [
  {
    pattern:
      /sainsbury|tesco|waitrose|lidl|aldi|asda|morrisons|whole\s*foods|co-op|budgens|marks.*partner|m&s\s*food|zabka|udderlicious|dusty\s*knuckle|avoman|venchi|cocoamore/i,
    category: "Groceries",
  },
  {
    pattern:
      /trainline|easyjet|ryanair|british\s*airways|tfl\s*travel|lul\s*ticket|transport\s*for\s*london|lime\*ride|lime\*\d|uber/i,
    category: "Transport",
  },
  {
    pattern: /vanguard|coinbase/i,
    category: "Investments",
  },
  {
    pattern: /deliveroo|uber.?eats|wingstop/i,
    category: "Takeout",
  },
];

function resolveCategory(monzoCategory: string, merchantName: string): string {
  for (const { pattern, category } of MERCHANT_OVERRIDES) {
    if (pattern.test(merchantName)) return category;
  }
  return MONZO_API_CATEGORY_MAP[monzoCategory] ?? "Uncategorised";
}

function resolveCategoryByMerchant(merchantName: string): string {
  for (const { pattern, category } of MERCHANT_OVERRIDES) {
    if (pattern.test(merchantName)) return category;
  }
  return "Uncategorised";
}

// ─── Owner detection ─────────────────────────────────────────────────────────

const ALEX_PATTERNS = [/mackintosh/i, /\balex\b/i];
const CASEY_PATTERNS = [/liddy/i, /\bcasey\b/i];

function resolveOwner(
  categoryName: string,
  merchantName: string,
  defaultOwner: "Alex" | "Casey" | "Joint" = "Joint",
): "Alex" | "Casey" | "Joint" {
  if (categoryName === "Net") {
    for (const p of ALEX_PATTERNS) if (p.test(merchantName)) return "Alex";
    for (const p of CASEY_PATTERNS) if (p.test(merchantName)) return "Casey";
  }
  return defaultOwner;
}

async function findCategory(name: string) {
  return (
    (await db.category.findUnique({ where: { name } })) ??
    (await db.category.findUniqueOrThrow({ where: { name: "Uncategorised" } }))
  );
}

// ─── Shared date helpers ─────────────────────────────────────────────────────

const MONTH_IDX: Record<string, number> = {
  Jan: 0,
  Feb: 1,
  Mar: 2,
  Apr: 3,
  May: 4,
  Jun: 5,
  Jul: 6,
  Aug: 7,
  Sep: 8,
  Oct: 9,
  Nov: 10,
  Dec: 11,
};

function inferYear(txMonthIdx: number, stmtMonth: number, stmtYear: number): number {
  // If tx month (1-based) > statement month (1-based), transaction is from prior year
  return txMonthIdx + 1 > stmtMonth ? stmtYear - 1 : stmtYear;
}

function toIsoDateShort(
  month: string,
  day: string | number,
  stmtMonth: number,
  stmtYear: number,
): string {
  const idx = MONTH_IDX[month];
  const year = inferYear(idx, stmtMonth, stmtYear);
  return `${year}-${String(idx + 1).padStart(2, "0")}-${String(+day).padStart(2, "0")}`;
}

// ─── Amex PDF parser ─────────────────────────────────────────────────────────
//
// pdf-parse extracts text column by column (descriptions first, amounts second).
// Transaction lines: "Jan5Jan5MERCHANT NAME" — no spaces between month/day.
// A standalone "CR" line marks the next transaction as a credit.
// Amount block (after "Amount  £") contains foreign amounts first, then N GBP amounts.
// We take the last N numbers as GBP amounts for N descriptions on that page.

const AMEX_PAGE_HEADER_RE = /^(?:(?:MR|MRS|MS|MISS|DR)\s+)?[A-Z].*\d{2}\/\d{2}\/\d{2}$/;
const AMEX_TX_LINE_RE =
  /^(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)(\d{1,2})(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)(\d{1,2})(.+)/;
const AMEX_PURE_AMOUNT_RE = /^[\d,]+\.\d{2}$/;
const AMEX_AMOUNT_HEADER = "Amount  £";
const AMEX_SENTINEL = "Total new spend transactions";

interface AmexRow {
  transactionDate: string;
  processDate: string;
  description: string;
  amount: string;
  isCredit: boolean;
  foreignCurrency: string | null;
  foreignAmount: string | null;
  statementDate: string;
}

interface AmexPage {
  descriptions: { txDate: string; procDate: string; desc: string; isCredit: boolean }[];
  amounts: string[];
}

function parseAmexPdf(text: string): AmexRow[] {
  const lines = text
    .split("\n")
    .map((l) => l.trim())
    .filter((l) => l.length > 0);

  let statementDate = "";
  for (const line of lines) {
    if (AMEX_PAGE_HEADER_RE.test(line)) {
      const m = line.match(/(\d{2}\/\d{2}\/\d{2})$/);
      if (m) {
        statementDate = m[1];
        break;
      }
    }
  }
  if (!statementDate) throw new Error("Could not find statement date in PDF");

  const parts = statementDate.split("/");
  const stmtMonth = parseInt(parts[1], 10);
  const stmtYear = 2000 + parseInt(parts[2], 10);

  const pages: AmexPage[] = [];
  let currentPage: AmexPage | null = null;
  let inAmountBlock = false;
  let prevWasCR = false;

  for (const line of lines) {
    if (line.startsWith(AMEX_SENTINEL)) break;

    if (AMEX_PAGE_HEADER_RE.test(line)) {
      currentPage = { descriptions: [], amounts: [] };
      pages.push(currentPage);
      inAmountBlock = false;
      prevWasCR = false;
      continue;
    }

    if (!currentPage) continue;

    if (line === AMEX_AMOUNT_HEADER) {
      inAmountBlock = true;
      continue;
    }

    if (inAmountBlock) {
      if (AMEX_PURE_AMOUNT_RE.test(line)) currentPage.amounts.push(line);
      continue;
    }

    if (line === "CR") {
      prevWasCR = true;
      continue;
    }

    const m = line.match(AMEX_TX_LINE_RE);
    if (m) {
      const [, txMonth, txDay, procMonth, procDay, desc] = m;
      const isCredit = prevWasCR || /PAYMENT RECEIVED/i.test(desc);
      currentPage.descriptions.push({
        txDate: toIsoDateShort(txMonth, txDay, stmtMonth, stmtYear),
        procDate: toIsoDateShort(procMonth, procDay, stmtMonth, stmtYear),
        desc: desc.trim(),
        isCredit,
      });
      prevWasCR = false;
    }
  }

  const rows: AmexRow[] = [];
  for (const page of pages) {
    const N = page.descriptions.length;
    if (N === 0) continue;
    const gbpAmounts = page.amounts.slice(-N);
    for (let i = 0; i < N; i++) {
      const d = page.descriptions[i];
      const amount = gbpAmounts[i];
      if (!amount) continue;
      rows.push({
        transactionDate: d.txDate,
        processDate: d.procDate,
        description: d.desc,
        amount,
        isCredit: d.isCredit,
        foreignCurrency: null,
        foreignAmount: null,
        statementDate,
      });
    }
  }

  return rows;
}

// ─── POST /api/admin/import/amex ─────────────────────────────────────────────

const VALID_OWNERS = new Set(["Alex", "Casey", "Joint"]);

// ─── Business keys (Barclays / Santander) ────────────────────────────────────
//
// Build a deterministic content-hash id per staged row so re-uploading the same
// file produces the SAME ids and the rows are detected as duplicates instead of
// being inserted again. Genuine same-content repeats within ONE file (e.g. four
// £1.75 Lime hires on the same day) are kept distinct by appending _1, _2, _3…
// Rows are sorted by their content key first, so suffix assignment is independent
// of PDF parse order — the same file always yields the same set of ids.
function assignBusinessKeys<T>(
  rows: T[],
  contentKey: (r: T) => string,
): (T & { transactionId: string })[] {
  const sorted = [...rows].sort((a, b) => contentKey(a).localeCompare(contentKey(b)));
  const counts = new Map<string, number>();
  return sorted.map((r) => {
    const baseId = createHash("sha256").update(contentKey(r)).digest("hex").slice(0, 16);
    const c = counts.get(baseId) ?? 0;
    counts.set(baseId, c + 1);
    return { ...r, transactionId: c === 0 ? baseId : `${baseId}_${c}` };
  });
}

function amexTransactionId(row: AmexRow): string {
  const sign = row.isCredit ? "CR" : "DR";
  return createHash("sha256")
    .update(`${row.transactionDate}|${row.processDate}|${row.description}|${row.amount}|${sign}`)
    .digest("hex")
    .slice(0, 16);
}

importRouter.post("/import/amex", upload.single("file"), async (req, res) => {
  if (!req.file) {
    res.status(400).json({ error: "No file uploaded" });
    return;
  }
  const owner = VALID_OWNERS.has(req.body.owner) ? req.body.owner : "Alex";

  let text: string;
  try {
    text = (await pdfParse(req.file.buffer)).text;
  } catch {
    res.status(400).json({ error: "Failed to extract text from PDF" });
    return;
  }

  const statementCheck = statementSchemas.amex.safeParse(text);
  if (!statementCheck.success) {
    res.status(422).json({ error: statementCheck.error.issues[0].message });
    return;
  }

  let rows: AmexRow[];
  try {
    rows = parseAmexPdf(text);
  } catch (err) {
    res
      .status(400)
      .json({ error: err instanceof Error ? err.message : "Failed to parse Amex statement" });
    return;
  }

  const existing = await db.amexTransaction.findMany({ select: { transactionId: true } });
  const existingIds = new Set(existing.map((r) => r.transactionId));

  const duplicates: string[] = [];
  const toInsert: (AmexRow & { transactionId: string })[] = [];
  const batchCounts = new Map<string, number>();

  for (const row of rows) {
    const baseId = amexTransactionId(row);
    const count = batchCounts.get(baseId) ?? 0;
    batchCounts.set(baseId, count + 1);
    const transactionId = count === 0 ? baseId : `${baseId}-${count}`;

    if (count > 0) {
      console.log(
        `[amex] within-statement duplicate #${count}: ${row.transactionDate} "${row.description}" £${row.amount} → ${transactionId}`,
      );
    }

    if (existingIds.has(transactionId)) {
      duplicates.push(transactionId);
    } else {
      toInsert.push({ ...row, transactionId });
    }
  }

  if (toInsert.length > 0)
    await db.amexTransaction.createMany({ data: toInsert.map((r) => ({ ...r, owner })) });
  res.json({ imported: toInsert.length, duplicates });
});

// ─── Barclays PDF parser ──────────────────────────────────────────────────────
//
// Each transaction is one logical entry: "DD MonMERCHANT NAME£AMOUNT"
// Sometimes the amount wraps to the next line (starts with £).
// Standalone "e" lines are contactless markers — skip them.
// Description continuation lines are appended to the current transaction.
// Statement date from footer: "Page 1 of 4 // issued on DD Month YYYY"

const BARCLAYS_TX_RE = /^(\d{2})\s+(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)(.+)/;
const BARCLAYS_AMOUNT_RE = /£([\d,]+\.\d{2})$/;
const BARCLAYS_AMOUNT_LINE_RE = /^£([\d,]+\.\d{2})$/;
const BARCLAYS_ISSUED_RE = /issued on (\d+)\s+(\w+)\s+(\d{4})/;
const BARCLAYS_SENTINEL_RE = /^(Promotional transactions|Your new balance|Interest and charges)/;

const FULL_MONTH: Record<string, number> = {
  January: 1,
  February: 2,
  March: 3,
  April: 4,
  May: 5,
  June: 6,
  July: 7,
  August: 8,
  September: 9,
  October: 10,
  November: 11,
  December: 12,
  Jan: 1,
  Feb: 2,
  Mar: 3,
  Apr: 4,
  Jun: 6,
  Jul: 7,
  Aug: 8,
  Sep: 9,
  Oct: 10,
  Nov: 11,
  Dec: 12,
};

interface BarclaysRow {
  date: string;
  description: string;
  amount: string;
  isCredit: boolean;
  statementDate: string;
}

function parseBarclaysPdf(text: string): BarclaysRow[] {
  const lines = text
    .split("\n")
    .map((l) => l.trim())
    .filter((l) => l.length > 0);

  // Extract statement month+year from issued date in footer
  let stmtMonth = 0;
  let stmtYear = 0;
  let statementDate = "";
  for (const line of lines) {
    const m = line.match(BARCLAYS_ISSUED_RE);
    if (m) {
      stmtMonth = FULL_MONTH[m[2]] ?? 0;
      stmtYear = parseInt(m[3], 10);
      statementDate = `${m[2]} ${m[3]}`;
      break;
    }
  }
  if (!stmtMonth) throw new Error("Could not find statement date in Barclays PDF");

  const rows: BarclaysRow[] = [];
  let current: Partial<BarclaysRow> | null = null;
  let inTransactions = false;

  function flush() {
    if (current?.date && current.description && current.amount !== undefined) {
      rows.push(current as BarclaysRow);
    }
    current = null;
  }

  for (const line of lines) {
    // Start collecting after the "How you've used your card" header
    if (line === "How you've used your card") {
      inTransactions = true;
      continue;
    }
    if (!inTransactions) continue;

    // Stop at end of spend section
    if (BARCLAYS_SENTINEL_RE.test(line)) {
      flush();
      break;
    }

    // Skip contactless marker and other noise lines
    if (line === "e" || line === "m" || line.startsWith("Page ")) continue;

    // New transaction line
    const txMatch = line.match(BARCLAYS_TX_RE);
    if (txMatch) {
      flush();
      const [, day, month, rest] = txMatch;
      const date = toIsoDateShort(month, day, stmtMonth, stmtYear);
      const amountMatch = rest.match(BARCLAYS_AMOUNT_RE);
      if (amountMatch) {
        const desc = rest.slice(0, rest.lastIndexOf("£")).trim();
        const isCredit = /Payment By Direct Debit/i.test(desc);
        current = { date, description: desc, amount: amountMatch[1], isCredit, statementDate };
      } else {
        current = { date, description: rest.trim(), isCredit: false, statementDate };
      }
      continue;
    }

    if (!current) continue;

    // Amount on its own line
    const amtMatch = line.match(BARCLAYS_AMOUNT_LINE_RE);
    if (amtMatch) {
      current.amount = amtMatch[1];
      current.isCredit = /Payment By Direct Debit/i.test(current.description ?? "");
      continue;
    }

    // Description continuation (e.g. Amazon.co.uk wrapping, or Sunbury-On-the "e" suffix)
    if (!current.amount) {
      current.description = (current.description ?? "") + " " + line;
    }
  }

  flush();
  return rows;
}

// ─── POST /api/admin/import/barclays ─────────────────────────────────────────

importRouter.post("/import/barclays", upload.single("file"), async (req, res) => {
  if (!req.file) {
    res.status(400).json({ error: "No file uploaded" });
    return;
  }
  const owner = VALID_OWNERS.has(req.body.owner) ? req.body.owner : "Alex";

  let text: string;
  try {
    text = (await pdfParse(req.file.buffer)).text;
  } catch {
    res.status(400).json({ error: "Failed to extract text from PDF" });
    return;
  }

  const statementCheck = statementSchemas.barclays.safeParse(text);
  if (!statementCheck.success) {
    res.status(422).json({ error: statementCheck.error.issues[0].message });
    return;
  }

  let rows: BarclaysRow[];
  try {
    rows = parseBarclaysPdf(text);
  } catch (err) {
    res
      .status(400)
      .json({ error: err instanceof Error ? err.message : "Failed to parse Barclays statement" });
    return;
  }

  const keyed = assignBusinessKeys(
    rows,
    (r) => `${r.date}|${r.description}|${r.amount}|${r.isCredit}`,
  );

  const existing = await db.barclaysTransaction.findMany({ select: { transactionId: true } });
  const existingIds = new Set(existing.map((r) => r.transactionId));

  const toInsert: (BarclaysRow & { transactionId: string; owner: string })[] = [];
  const duplicates: string[] = [];
  for (const row of keyed) {
    if (existingIds.has(row.transactionId)) duplicates.push(row.transactionId);
    else toInsert.push({ ...row, owner });
  }

  if (toInsert.length > 0) await db.barclaysTransaction.createMany({ data: toInsert });
  res.json({ imported: toInsert.length, duplicates });
});

// ─── Santander PDF parser ─────────────────────────────────────────────────────
//
// Current account (not credit card) — has both money in and money out.
// Transaction lines: "DDth MonDESCRIPTION[amount][balance]" all concatenated.
// Some entries span multiple lines (long descriptions).
//
// The PDF concatenates mandate numbers directly into amounts (e.g. "MANDATE NO
// 0023257.003,898.99" where the true amount is 257.00). We sidestep this by
// extracting only the balance (last [\d,]+\.\d{2}) and deriving the transaction
// amount from the running balance difference instead of parsing it from text.

const SANTANDER_TX_RE =
  /^(\d{1,2})(?:st|nd|rd|th)\s+(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)(.*)/;
const SANTANDER_PERIOD_RE =
  /^(\d{1,2})\w{2}\s+(\w+)\s+(\d{4})\s+to\s+(\d{1,2})\w{2}\s+(\w+)\s+(\d{4})$/i;
const SANTANDER_ONE_AMOUNT_RE = /([\d,]+\.\d{2})$/;
const SANTANDER_SKIP_RE =
  /^(Balance brought forward|Balance carried forward|Average credit balance)/i;

// Match a single properly-formatted currency amount: 1–3 digits, optional comma-thousands groups, decimal.
// Used to find the LAST (rightmost) amount in a concatenated string so we can extract the balance
// without being confused by mandate numbers (e.g. "0023257.003,898.99" → balance is "3,898.99").
const SANTANDER_CCY_RE = /\d{1,3}(?:,\d{3})*\.\d{2}/g;

interface SantanderRow {
  date: string;
  description: string;
  moneyIn: string | null;
  moneyOut: string | null;
  balance: string;
  statementDate: string;
}

function parseAmount(s: string): number {
  return Math.round(parseFloat(s.replace(/,/g, "")) * 100);
}

// Find the last properly-formatted currency amount in s and treat it as the balance.
// Uses SANTANDER_CCY_RE to avoid matching mandate numbers (e.g. "0023") as part of
// the amount — "0023257.003,898.99" correctly yields balance "3,898.99".
// Strips any trailing digits/commas/dots from the pre-balance text to remove the
// parsed transaction amount, leaving a clean description.
function splitDescBalance(s: string): { desc: string; balance: string } | null {
  const matches = [...s.matchAll(new RegExp(SANTANDER_CCY_RE.source, "g"))];
  const last = matches.at(-1);
  if (!last || last.index! + last[0].length !== s.length) return null;
  const pre = s.slice(0, last.index!);
  const desc = pre.replace(/[\d,.]+$/, "").trim();
  return { desc, balance: last[0] };
}

function parseSantanderPdf(text: string): SantanderRow[] {
  const lines = text
    .split("\n")
    .map((l) => l.trim())
    .filter((l) => l.length > 0);

  // Extract statement period to determine year boundaries
  let stmtMonth = 0;
  let stmtYear = 0;
  let statementDate = "";
  for (const line of lines) {
    const m = line.match(SANTANDER_PERIOD_RE);
    if (m) {
      // Use end date (m[4], m[5], m[6]) for year inference
      stmtMonth = FULL_MONTH[m[5]] ?? 0;
      stmtYear = parseInt(m[6], 10);
      statementDate = `${m[1]} ${m[2]} ${m[3]} to ${m[4]} ${m[5]} ${m[6]}`;
      break;
    }
  }
  if (!stmtMonth) throw new Error("Could not find statement period in Santander PDF");

  const rows: SantanderRow[] = [];
  let current: { date: string; description: string } | null = null;
  let prevBalance = 0; // in pence
  let inTransactions = false;

  // Derive amount from balance difference — avoids parsing mandate-prefixed amounts.
  function flush(balanceStr: string) {
    if (!current) return;
    const balance = parseAmount(balanceStr);
    const diff = balance - prevBalance;
    const moneyIn = diff > 0 ? (diff / 100).toFixed(2) : null;
    const moneyOut = diff < 0 ? (-diff / 100).toFixed(2) : null;
    prevBalance = balance;
    rows.push({
      date: current.date,
      description: current.description.trim(),
      moneyIn,
      moneyOut,
      balance: balanceStr,
      statementDate,
    });
    current = null;
  }

  for (const line of lines) {
    if (line.startsWith("Your transactions")) {
      inTransactions = true;
    }
    if (!inTransactions) continue;

    // Skip table header
    if (/^DateDescriptionMoney inMoney out/.test(line)) continue;

    // Opening balance — extract and set prevBalance, skip as transaction
    if (/Balance brought forward from previous statement/.test(line)) {
      const m = line.match(SANTANDER_ONE_AMOUNT_RE);
      if (m) prevBalance = parseAmount(m[1]);
      continue;
    }

    // Closing balance — stop
    if (/Balance carried forward/.test(line)) {
      current = null;
      break;
    }

    // Skip average balance line
    if (SANTANDER_SKIP_RE.test(line)) continue;

    // New transaction line?
    const txMatch = line.match(SANTANDER_TX_RE);
    if (txMatch) {
      const [, day, month, rest] = txMatch;
      const date = toIsoDateShort(month, day, stmtMonth, stmtYear);
      const parsed = splitDescBalance(rest);
      if (parsed && !SANTANDER_SKIP_RE.test(parsed.desc)) {
        current = { date, description: parsed.desc };
        flush(parsed.balance);
      } else {
        // No amounts yet — multi-line entry (description continues on next lines)
        current = { date, description: rest.trim() };
      }
      continue;
    }

    // Not a date line — description continuation or standalone amounts line
    if (current) {
      if (!/[a-zA-Z]/.test(line)) {
        // No letters — could be a pure-amounts line (e.g. "2,400.003,475.57") or a
        // mandate/reference number on its own line (e.g. "0101"). Try to extract balance.
        const parsed = splitDescBalance(line);
        if (parsed) {
          flush(parsed.balance);
        } else {
          current.description += " " + line;
        }
      } else {
        current.description += " " + line;
      }
    }
  }

  return rows;
}

// ─── POST /api/admin/import/santander ────────────────────────────────────────

importRouter.post("/import/santander", upload.single("file"), async (req, res) => {
  if (!req.file) {
    res.status(400).json({ error: "No file uploaded" });
    return;
  }
  const owner = VALID_OWNERS.has(req.body.owner) ? req.body.owner : "Alex";

  let text: string;
  try {
    text = (await pdfParse(req.file.buffer)).text;
  } catch {
    res.status(400).json({ error: "Failed to extract text from PDF" });
    return;
  }

  const statementCheck = statementSchemas.santander.safeParse(text);
  if (!statementCheck.success) {
    res.status(422).json({ error: statementCheck.error.issues[0].message });
    return;
  }

  let rows: SantanderRow[];
  try {
    rows = parseSantanderPdf(text);
  } catch (err) {
    res
      .status(400)
      .json({ error: err instanceof Error ? err.message : "Failed to parse Santander statement" });
    return;
  }

  const keyed = assignBusinessKeys(
    rows,
    (r) => `${r.date}|${r.description}|${r.moneyIn ?? ""}|${r.moneyOut ?? ""}|${r.balance}`,
  );

  const existing = await db.santanderTransaction.findMany({ select: { transactionId: true } });
  const existingIds = new Set(existing.map((r) => r.transactionId));

  const toInsert: (SantanderRow & { transactionId: string; owner: string })[] = [];
  const duplicates: string[] = [];
  for (const row of keyed) {
    if (existingIds.has(row.transactionId)) duplicates.push(row.transactionId);
    else toInsert.push({ ...row, owner });
  }

  if (toInsert.length > 0) await db.santanderTransaction.createMany({ data: toInsert });
  res.json({ imported: toInsert.length, duplicates });
});

// ─── HSBC PDF parser ──────────────────────────────────────────────────────────
//
// pdf-parse reads HSBC statements column by column, so transactions appear
// BEFORE "BALANCE BROUGHT FORWARD" in the extracted text.
// Transaction lines: "DD Mon YY TYPE Description" — date optional (inherited when absent).
// Multi-line entries: name on one line, reference+amount on the next.
// CR type = money in; all other types = money out.
// Balance (when present) = last number on the final line of a transaction group.

const HSBC_DATE_RE =
  /^(\d{2})\s+(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+(\d{2})\s*(.*)/;
const HSBC_TYPE_RE = /^(BP|OBP|CR|DD|SO|TFR|VIS|ATM|CHQ|FP|DEB|BGC|STO|DR)\s*(.*)/;
// Properly-formatted currency: 1-3 digits, optional comma-thousand groups, 2 decimal places.
// This avoids matching reference numbers (e.g. "00021370249583") that don't follow the 1-3 digit prefix rule.
const HSBC_CCY_RE = /\d{1,3}(?:,\d{3})*\.\d{2}/g;
const HSBC_PERIOD_RE = /(\d{1,2})\s+(\w+)(?:\s+(\d{4}))?\s+to\s+(\d{1,2})\s+(\w+)\s+(\d{4})/i;
const HSBC_SENTINEL = "BALANCE BROUGHT FORWARD";

// Extract the last two properly-formatted currency amounts from a line.
// Primary: split on whitespace and find tokens that are exactly a currency value.
// This prevents reference numbers whose trailing digits fuse with an amount
// (e.g. "vrp0002137024958 3,000.00 5,144.88" → tokens → "3,000.00", "5,144.88").
// Fallback: scan for the pattern anywhere in the string, requiring the last match to
// reach end-of-string. Handles amounts fused directly to descriptions with no space
// (e.g. "SKY TV51.0011,867.05").
const HSBC_CCY_EXACT = /^\d{1,3}(?:,\d{3})*\.\d{2}$/;

function extractHsbcAmounts(s: string): {
  desc: string;
  amount: string | null;
  balance: string | null;
} {
  // Primary: token-based (requires spaces between columns)
  const tokens = s.split(/\s+/);
  const amtIdxs = tokens.reduce<number[]>(
    (acc, t, i) => (HSBC_CCY_EXACT.test(t) ? [...acc, i] : acc),
    [],
  );

  if (amtIdxs.length >= 2) {
    const desc = tokens.slice(0, amtIdxs.at(-2)!).join(" ").trim();
    return {
      desc: desc || s.trim(),
      amount: tokens[amtIdxs.at(-2)!],
      balance: tokens[amtIdxs.at(-1)!],
    };
  }
  if (amtIdxs.length === 1) {
    const desc = tokens.slice(0, amtIdxs[0]).join(" ").trim();
    return { desc: desc || s.trim(), amount: tokens[amtIdxs[0]], balance: null };
  }

  // Fallback: scan (handles fused descriptions like "SKY TV51.0011,867.05")
  const matches = [...s.matchAll(new RegExp(HSBC_CCY_RE.source, "g"))];
  if (matches.length === 0) return { desc: s.trim(), amount: null, balance: null };

  const last = matches.at(-1)!;
  if (last.index! + last[0].length !== s.length)
    return { desc: s.trim(), amount: null, balance: null };

  if (matches.length >= 2) {
    const second = matches.at(-2)!;
    const desc = s.slice(0, second.index!).trim();
    return { desc: desc || s.trim(), amount: second[0], balance: last[0] };
  }

  const desc = s.slice(0, last.index!).trim();
  return { desc: desc || s.trim(), amount: last[0], balance: null };
}

interface HsbcRow {
  date: string;
  paymentType: string;
  description: string;
  moneyOut: string | null;
  moneyIn: string | null;
  balance: string | null;
  statementDate: string;
}

interface HsbcPending {
  date: string;
  paymentType: string;
  lines: string[]; // raw text lines accumulated (date+type prefix already stripped)
}

function parseHsbcPdf(text: string): HsbcRow[] {
  const allLines = text
    .split("\n")
    .map((l) => l.trim())
    .filter((l) => l.length > 0);

  // Statement date appears after the transactions in pdf-parse output
  let statementDate = "";
  let stmtMonth = 0;
  let stmtYear = 0;
  for (const line of allLines) {
    const m = line.match(HSBC_PERIOD_RE);
    if (m) {
      stmtMonth = FULL_MONTH[m[5]] ?? 0;
      stmtYear = parseInt(m[6], 10);
      statementDate = m[3]
        ? `${m[1]} ${m[2]} ${m[3]} to ${m[4]} ${m[5]} ${m[6]}`
        : `${m[1]} ${m[2]} to ${m[4]} ${m[5]} ${m[6]}`;
      break;
    }
  }
  if (!stmtMonth) throw new Error("Could not find statement period in HSBC PDF");

  const rows: HsbcRow[] = [];
  let current: HsbcPending | null = null;
  let currentDate = "";

  function flush() {
    if (!current || current.lines.length === 0) {
      current = null;
      return;
    }

    // Combine all lines; extract trailing currency amounts
    const full = current.lines.join(" ");
    const { desc: rawDesc, amount, balance } = extractHsbcAmounts(full);
    const desc = rawDesc.replace(/\s+/g, " ").trim();

    const isCredit = current.paymentType === "CR";
    rows.push({
      date: current.date,
      paymentType: current.paymentType,
      description: desc || current.paymentType,
      moneyOut: !isCredit && amount ? amount : null,
      moneyIn: isCredit && amount ? amount : null,
      balance,
      statementDate,
    });
    current = null;
  }

  for (const line of allLines) {
    if (line.includes(HSBC_SENTINEL)) {
      flush();
      break;
    }

    // Skip noise lines (table header, address, etc.) before transactions start
    if (
      /^(Date\s+Paym|Opening Balance|Payments In|Payments Out|Closing Balance|Your HSBC|Contact tel|Text phone|www\.|Account Summary|International Bank|Bank Identifier|Account Name)/i.test(
        line,
      )
    )
      continue;

    // Check for date prefix
    const dateMatch = line.match(HSBC_DATE_RE);
    if (dateMatch) {
      const [, day, month, yr, rest] = dateMatch;
      currentDate = toIsoDateShort(month, day, stmtMonth, stmtYear);
      // Ignore override: 2-digit year → 2000+yr
      const fullYear = 2000 + parseInt(yr, 10);
      const monthIdx = MONTH_IDX[month];
      currentDate = `${fullYear}-${String(monthIdx + 1).padStart(2, "0")}-${String(+day).padStart(2, "0")}`;

      const typeMatch = rest.match(HSBC_TYPE_RE);
      if (typeMatch) {
        flush();
        const [, type, remainder] = typeMatch;
        current = { date: currentDate, paymentType: type, lines: remainder ? [remainder] : [] };
      } else if (/^BALANCE/.test(rest)) {
        // BALANCE BROUGHT/CARRIED FORWARD lines — skip
        continue;
      } else if (rest.length > 0) {
        // Date line with no type code — unlikely but append to current
        if (current) current.lines.push(rest);
      }
      continue;
    }

    // Check for type-only line (no date)
    const typeMatch = line.match(HSBC_TYPE_RE);
    if (typeMatch) {
      flush();
      const [, type, remainder] = typeMatch;
      current = { date: currentDate, paymentType: type, lines: remainder ? [remainder] : [] };
      continue;
    }

    // Continuation line
    if (current) current.lines.push(line);
  }

  flush();
  return rows;
}

// ─── POST /api/admin/import/hsbc ─────────────────────────────────────────────

// Custom pdf-parse renderer for HSBC statements.
// The default renderer concatenates same-line items with no separator, causing
// reference numbers (e.g. vrp0002137024958) to fuse with the amount column.
// This renderer inserts a space whenever the X gap between adjacent items on
// the same line exceeds the width of the previous item (i.e. there's a column gap).
async function hsbcPageRender(pageData: {
  getTextContent: (
    opts?: object,
  ) => Promise<{ items: Array<{ str: string; transform: number[]; width?: number }> }>;
}) {
  const textContent = await pageData.getTextContent({
    normalizeWhitespace: false,
    disableCombineTextItems: false,
  });
  let lastY: number | null = null;
  let lastX: number | null = null;
  let lastWidth = 0;
  let text = "";
  for (const item of textContent.items) {
    const x = item.transform[4];
    const y = item.transform[5];
    if (lastY === null) {
      text += item.str;
    } else if (Math.abs(y - lastY) > 1) {
      text += "\n" + item.str;
    } else {
      const gap = x - (lastX! + lastWidth);
      text += (gap > 1 ? " " : "") + item.str;
    }
    lastY = y;
    lastX = x;
    lastWidth = item.width ?? 0;
  }
  return text;
}

importRouter.post("/import/hsbc", upload.single("file"), async (req, res) => {
  if (!req.file) {
    res.status(400).json({ error: "No file uploaded" });
    return;
  }
  const owner = VALID_OWNERS.has(req.body.owner) ? req.body.owner : "Joint";

  let text: string;
  try {
    text = (await pdfParse(req.file.buffer, { pagerender: hsbcPageRender })).text;
  } catch {
    res.status(400).json({ error: "Failed to extract text from PDF" });
    return;
  }

  // Guard against the wrong bank's statement (e.g. an Amex PDF) being uploaded here.
  const statementCheck = statementSchemas.hsbc.safeParse(text);
  if (!statementCheck.success) {
    res.status(422).json({ error: statementCheck.error.issues[0].message });
    return;
  }

  let rows: HsbcRow[];
  try {
    rows = parseHsbcPdf(text);
  } catch (err) {
    res
      .status(400)
      .json({ error: err instanceof Error ? err.message : "Failed to parse HSBC statement" });
    return;
  }

  const existing = await db.hsbcTransaction.findMany({ select: { transactionId: true } });
  const existingIds = new Set(existing.map((r) => r.transactionId));
  const batchCounts = new Map<string, number>();
  const toInsert: (HsbcRow & { transactionId: string; owner: string })[] = [];
  const duplicates: string[] = [];

  for (const row of rows) {
    const amount = row.moneyIn ?? row.moneyOut ?? "";
    const baseId = createHash("sha256")
      .update(`${row.date}|${row.paymentType}|${row.description}|${amount}`)
      .digest("hex")
      .slice(0, 16);
    const count = batchCounts.get(baseId) ?? 0;
    batchCounts.set(baseId, count + 1);
    const transactionId = count === 0 ? baseId : `${baseId}-${count}`;
    if (existingIds.has(transactionId)) duplicates.push(transactionId);
    else toInsert.push({ ...row, transactionId, owner });
  }

  if (toInsert.length > 0) await db.hsbcTransaction.createMany({ data: toInsert });
  res.json({ imported: toInsert.length, duplicates });
});

// ─── Chase PDF parser ────────────────────────────────────────────────────────
//
// Chase statements have clear column spacing in the raw pdf-parse output:
//   "MM/DD  Merchant Name or Description  123.45"
// We preserve whitespace (no trim) and use \s{2,} as the column delimiter.
// Payments/credits appear as negative amounts (-3,021.55); purchases are positive.
// Exchange-rate continuation lines (POUND STERLING / EXCHG RATE) don't match the
// transaction regex and are ignored automatically.

// MM/DD  <description>  <amount> — date on left, amount anchored at right.
// Primary: requires whitespace before amount (normal purchases).
// Fallback: payment rows fuse description directly into a negative amount with no space,
//   e.g. "01/12  Payment Thank You-Mobile-973.68" — description must end with a letter.
const CHASE_TX_RE = /^\s*(\d{2}\/\d{2})\s+(.*\S)\s+(-?[\d,]+\.\d{2})\s*$/;
const CHASE_PAYMENT_RE = /^\s*(\d{2}\/\d{2})\s+(.*[A-Za-z])(-[\d,]+\.\d{2})\s*$/;
// Closing date: "Opening/Closing Date 12/23/25 - 01/22/26"  or  "Statement Date: 01/22/26"
const CHASE_CLOSING_RE =
  /(?:Opening\/Closing Date.*?|Statement Date:\s*)(\d{2})\/(\d{2})\/(\d{2})\s*$/;

interface ChaseRow {
  date: string;
  description: string;
  amount: string;
  isCredit: boolean;
  statementDate: string;
}

function parseChasePdf(text: string): ChaseRow[] {
  // Deliberately NOT trimming lines — whitespace is the column separator
  const lines = text.split("\n");

  // Extract closing date for year inference and statement label
  let stmtMonth = 0;
  let stmtYear = 0;
  let statementDate = "";
  for (const line of lines) {
    const m = line.match(CHASE_CLOSING_RE);
    if (m) {
      stmtMonth = parseInt(m[1], 10);
      stmtYear = 2000 + parseInt(m[3], 10);
      statementDate = `${["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"][stmtMonth - 1]} ${stmtYear}`;
      break;
    }
  }
  if (!stmtMonth) throw new Error("Could not find closing date in Chase PDF");

  const rows: ChaseRow[] = [];

  for (const line of lines) {
    const m = line.match(CHASE_TX_RE) ?? line.match(CHASE_PAYMENT_RE);
    if (!m) continue;

    const [, mmdd, desc, rawAmount] = m;
    const description = desc.trim();

    // Skip exchange rate / currency continuation lines that slipped through
    if (/EXCHG RATE|POUND STERLING|ZLOTY|RINGGIT|EURO/i.test(description)) continue;
    // Skip interest charges section rows (no merchant, just APR info)
    if (/^\d+\.\d{2}%/.test(description)) continue;

    const [mm, dd] = mmdd.split("/").map(Number);
    const txMonthIdx = mm - 1;
    const year = inferYear(txMonthIdx, stmtMonth, stmtYear);
    const date = `${year}-${String(mm).padStart(2, "0")}-${String(dd).padStart(2, "0")}`;

    const isCredit = rawAmount.startsWith("-");
    const amount = rawAmount.replace(/^-/, "").replace(/,/g, "");

    rows.push({ date, description, amount, isCredit, statementDate });
  }

  return rows;
}

// ─── POST /api/admin/import/chase ────────────────────────────────────────────

importRouter.post("/import/chase", upload.single("file"), async (req, res) => {
  if (!req.file) {
    res.status(400).json({ error: "No file uploaded" });
    return;
  }
  const owner = VALID_OWNERS.has(req.body.owner) ? req.body.owner : "Casey";

  let text: string;
  try {
    text = (await pdfParse(req.file.buffer)).text;
  } catch {
    res.status(400).json({ error: "Failed to extract text from PDF" });
    return;
  }

  const statementCheck = statementSchemas.chase.safeParse(text);
  if (!statementCheck.success) {
    res.status(422).json({ error: statementCheck.error.issues[0].message });
    return;
  }

  let rows: ChaseRow[];
  try {
    rows = parseChasePdf(text);
  } catch (err) {
    res
      .status(400)
      .json({ error: err instanceof Error ? err.message : "Failed to parse Chase statement" });
    return;
  }

  if (rows.length === 0) {
    res.status(422).json({ error: "0 rows parsed from Chase PDF" });
    return;
  }

  const existing = await db.chaseTransaction.findMany({ select: { transactionId: true } });
  const existingIds = new Set(existing.map((r) => r.transactionId));
  const batchCounts = new Map<string, number>();
  const toInsert: (ChaseRow & { transactionId: string; owner: string })[] = [];
  const duplicates: string[] = [];

  for (const row of rows) {
    const baseId = createHash("sha256")
      .update(`${row.date}|${row.description}|${row.amount}|${row.isCredit}`)
      .digest("hex")
      .slice(0, 16);
    const count = batchCounts.get(baseId) ?? 0;
    batchCounts.set(baseId, count + 1);
    const transactionId = count === 0 ? baseId : `${baseId}-${count}`;

    if (existingIds.has(transactionId)) duplicates.push(transactionId);
    else toInsert.push({ ...row, transactionId, owner });
  }

  if (toInsert.length > 0) await db.chaseTransaction.createMany({ data: toInsert });
  res.json({ imported: toInsert.length, duplicates });
});

// ─── SoFi PDF parser ─────────────────────────────────────────────────────────
//
// SoFi statements are standard text PDFs with a simple table:
//   DATE  TYPE  DESCRIPTION  AMOUNT  BALANCE
// Each row is followed by "Transaction ID: XXX" on the next line, then
// the amount + balance on the line after (e.g. "-$823.93 $374.41").
// A single PDF may contain both Checking and Savings accounts.
// Internal SoFi transfers (Savings→Checking etc.) are skipped during processing.

const SOFI_TX_ID_RE = /^Transaction ID:\s+(.+)$/;
// pdf-parse fuses columns with no separator, so date+type+desc run together:
// "Jan 31, 2026Interest EarnedInterest earned"
// Amount+balance likewise: "$0.72$375.13" or "-$823.93$374.41"
const SOFI_DATE_COMBINED_RE =
  /^(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+(\d{1,2}),\s+(\d{4})(Interest Earned|Direct Payment|Deposit|Withdrawal|Instant Transfer)(.*)/;
const SOFI_DATE_ONLY_RE =
  /^(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+(\d{1,2}),\s+(\d{4})$/;
const SOFI_TYPE_RE = /^(Interest Earned|Direct Payment|Deposit|Withdrawal|Instant Transfer)$/;
const SOFI_AMOUNT_BALANCE_RE = /^(-?\$[\d,]+\.\d{2})\$([\d,]+\.\d{2})$/;
const SOFI_PERIOD_RE =
  /(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d+,\s+\d{4}\s*[-–]\s*(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d+,\s+(\d{4})/;

interface SofiRow {
  transactionId: string;
  date: string;
  type: string;
  description: string;
  amount: string;
  isCredit: boolean;
  balance: string | null;
  accountType: string;
  statementDate: string;
}

function parseSofiPdf(text: string): SofiRow[] {
  const lines = text
    .split("\n")
    .map((l) => l.trim())
    .filter((l) => l.length > 0);

  // Extract statement end date ("Jan 2026") from "Jan 1, 2026 - Jan 31, 2026"
  let statementDate = "";
  for (const line of lines) {
    const m = line.match(SOFI_PERIOD_RE);
    if (m) {
      statementDate = `${m[2]} ${m[3]}`;
      break;
    }
  }

  const rows: SofiRow[] = [];
  let currentAccountType = "Checking";

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];

    // Track which account section we're in
    if (/^Checking Account\s*-\s*\d+$/.test(line)) {
      currentAccountType = "Checking";
      continue;
    }
    if (/^Savings Account\s*-\s*\d+$/.test(line)) {
      currentAccountType = "Savings";
      continue;
    }

    const txIdMatch = line.match(SOFI_TX_ID_RE);
    if (!txIdMatch) continue;

    const transactionId = txIdMatch[1].trim();
    let date = "";
    let type = "";
    let description = "";

    // Scan backward up to 5 lines to find the date line
    for (let j = i - 1; j >= Math.max(0, i - 5); j--) {
      const prev = lines[j];
      if (SOFI_TX_ID_RE.test(prev)) break; // hit another transaction's ID

      // Case A: "Jan 31, 2026 Interest Earned Interest earned" — all on one line
      const combinedM = prev.match(SOFI_DATE_COMBINED_RE);
      if (combinedM) {
        const [, month, day, year, txType, desc] = combinedM;
        date = `${year}-${String(MONTH_IDX[month] + 1).padStart(2, "0")}-${String(+day).padStart(2, "0")}`;
        type = txType;
        description =
          desc.trim() ||
          lines
            .slice(j + 1, i)
            .join(" ")
            .trim();
        break;
      }

      // Case B: date on its own line
      const dateOnlyM = prev.match(SOFI_DATE_ONLY_RE);
      if (dateOnlyM) {
        const [, month, day, year] = dateOnlyM;
        date = `${year}-${String(MONTH_IDX[month] + 1).padStart(2, "0")}-${String(+day).padStart(2, "0")}`;
        // Lines between date and Transaction ID are type + description
        const between = lines.slice(j + 1, i);
        const typeCheck = between[0]?.match(SOFI_TYPE_RE);
        if (typeCheck) {
          type = typeCheck[1];
          description = between.slice(1).join(" ").trim();
        } else {
          description = between.join(" ").trim();
        }
        break;
      }
    }

    if (!date) continue;

    // Find amount + balance on the line(s) following the Transaction ID
    let amount = "";
    let balance: string | null = null;
    let isCredit = false;

    for (let k = i + 1; k < Math.min(lines.length, i + 4); k++) {
      const abM = lines[k].match(SOFI_AMOUNT_BALANCE_RE);
      if (abM) {
        const rawAmt = abM[1]; // e.g. "-$823.93" or "$0.72"
        isCredit = !rawAmt.startsWith("-");
        amount = rawAmt.replace(/^-?\$/, "").replace(/,/g, "");
        balance = abM[2].replace(/,/g, "");
        break;
      }
      if (
        SOFI_DATE_COMBINED_RE.test(lines[k]) ||
        SOFI_DATE_ONLY_RE.test(lines[k]) ||
        SOFI_TX_ID_RE.test(lines[k])
      )
        break;
    }

    if (!amount) continue;

    rows.push({
      transactionId,
      date,
      type: type || "Unknown",
      description: description || type || transactionId,
      amount,
      isCredit,
      balance,
      accountType: currentAccountType,
      statementDate,
    });
  }

  return rows;
}

// ─── POST /api/admin/import/sofi ─────────────────────────────────────────────

importRouter.post("/import/sofi", upload.single("file"), async (req, res) => {
  if (!req.file) {
    res.status(400).json({ error: "No file uploaded" });
    return;
  }
  const owner = VALID_OWNERS.has(req.body.owner) ? req.body.owner : "Casey";

  let text: string;
  try {
    text = (await pdfParse(req.file.buffer)).text;
  } catch {
    res.status(400).json({ error: "Failed to extract text from PDF" });
    return;
  }

  const statementCheck = statementSchemas.sofi.safeParse(text);
  if (!statementCheck.success) {
    res.status(422).json({ error: statementCheck.error.issues[0].message });
    return;
  }

  let rows: SofiRow[];
  try {
    rows = parseSofiPdf(text);
  } catch (err) {
    res
      .status(400)
      .json({ error: err instanceof Error ? err.message : "Failed to parse SoFi statement" });
    return;
  }

  const existing = await db.sofiTransaction.findMany({ select: { transactionId: true } });
  const existingIds = new Set(existing.map((r) => r.transactionId));
  const batchCounts = new Map<string, number>();
  const toInsert: (SofiRow & { owner: string })[] = [];
  const duplicates: string[] = [];

  for (const row of rows) {
    // Transaction IDs from the statement are already unique per SoFi, but dedupe within batch too
    const baseId = row.transactionId;
    const count = batchCounts.get(baseId) ?? 0;
    batchCounts.set(baseId, count + 1);
    const transactionId = count === 0 ? baseId : `${baseId}-${count}`;

    if (existingIds.has(transactionId)) {
      duplicates.push(transactionId);
    } else {
      toInsert.push({ ...row, transactionId, owner });
    }
  }

  if (toInsert.length > 0) await db.sofiTransaction.createMany({ data: toInsert });
  res.json({ imported: toInsert.length, duplicates });
});

// ─── GET /api/admin/staged ───────────────────────────────────────────────────

importRouter.get("/staged", async (_req, res) => {
  const [monzo, amex, barclays, santander, hsbc, sofi, chase, plaid] = await Promise.all([
    db.monzoApiTransaction.groupBy({ by: ["status"], _count: true }),
    db.amexTransaction.groupBy({ by: ["status", "owner"], _count: true }),
    db.barclaysTransaction.groupBy({ by: ["status", "owner"], _count: true }),
    db.santanderTransaction.groupBy({ by: ["status", "owner"], _count: true }),
    db.hsbcTransaction.groupBy({ by: ["status", "owner"], _count: true }),
    db.sofiTransaction.groupBy({ by: ["status", "owner"], _count: true }),
    db.chaseTransaction.groupBy({ by: ["status", "owner"], _count: true }),
    db.plaidTransaction.groupBy({ by: ["status", "owner"], _count: true }),
  ]);

  function toCounts(rows: { status: string; _count: number }[]) {
    const m: Record<string, number> = {};
    for (const r of rows) m[r.status] = (m[r.status] ?? 0) + r._count;
    return {
      pending: m.pending ?? 0,
      processed: m.processed ?? 0,
      skipped: m.skipped ?? 0,
      errored: m.errored ?? 0,
    };
  }

  function toOwnerCounts(rows: { status: string; owner: string; _count: number }[]) {
    const byOwner: Record<string, Record<string, number>> = {};
    for (const r of rows) {
      byOwner[r.owner] ??= {};
      byOwner[r.owner][r.status] = (byOwner[r.owner][r.status] ?? 0) + r._count;
    }
    return byOwner;
  }

  res.json({
    monzo: toCounts(monzo.map((r) => ({ status: r.status, _count: r._count }))),
    amex: {
      ...toCounts(amex.map((r) => ({ status: r.status, _count: r._count }))),
      byOwner: toOwnerCounts(
        amex.map((r) => ({ status: r.status, owner: r.owner, _count: r._count })),
      ),
    },
    barclays: {
      ...toCounts(barclays.map((r) => ({ status: r.status, _count: r._count }))),
      byOwner: toOwnerCounts(
        barclays.map((r) => ({ status: r.status, owner: r.owner, _count: r._count })),
      ),
    },
    santander: {
      ...toCounts(santander.map((r) => ({ status: r.status, _count: r._count }))),
      byOwner: toOwnerCounts(
        santander.map((r) => ({ status: r.status, owner: r.owner, _count: r._count })),
      ),
    },
    hsbc: {
      ...toCounts(hsbc.map((r) => ({ status: r.status, _count: r._count }))),
      byOwner: toOwnerCounts(
        hsbc.map((r) => ({ status: r.status, owner: r.owner, _count: r._count })),
      ),
    },
    sofi: {
      ...toCounts(sofi.map((r) => ({ status: r.status, _count: r._count }))),
      byOwner: toOwnerCounts(
        sofi.map((r) => ({ status: r.status, owner: r.owner, _count: r._count })),
      ),
    },
    chase: {
      ...toCounts(chase.map((r) => ({ status: r.status, _count: r._count }))),
      byOwner: toOwnerCounts(
        chase.map((r) => ({ status: r.status, owner: r.owner, _count: r._count })),
      ),
    },
    plaid: {
      ...toCounts(plaid.map((r) => ({ status: r.status, _count: r._count }))),
      byOwner: toOwnerCounts(
        plaid.map((r) => ({ status: r.status, owner: r.owner, _count: r._count })),
      ),
    },
  });
});

// ─── GET /api/admin/last-statement ───────────────────────────────────────────
// For each statement-based bank, the date (date-only) of the most recent
// processed Transaction. Tells the user how far their uploaded statements reach.

const BANK_EXTERNAL_ID_PREFIXES: Record<string, string> = {
  monzo: "monzo:",
  amex: "amex:",
  barclays: "barclays:",
  santander: "santander:",
  hsbc: "hsbc:",
  sofi: "sofi:",
  chase: "chase:",
};

importRouter.get("/last-statement", async (_req, res) => {
  const entries = await Promise.all(
    Object.entries(BANK_EXTERNAL_ID_PREFIXES).map(async ([bank, prefix]) => {
      const latest = await db.transaction.findFirst({
        where: { externalId: { startsWith: prefix } },
        orderBy: { date: "desc" },
        select: { date: true },
      });
      return [bank, latest ? latest.date.toISOString().slice(0, 10) : null] as const;
    }),
  );

  // Amex is shared between owners — break its latest date down per owner so the
  // import card can show the right "statements through" date for whoever is selected.
  const amexByOwnerRows = await db.transaction.groupBy({
    by: ["owner"],
    where: { externalId: { startsWith: "amex:" } },
    _max: { date: true },
  });
  const amexByOwner = Object.fromEntries(
    amexByOwnerRows.map(
      (r) => [r.owner, r._max.date ? r._max.date.toISOString().slice(0, 10) : null] as const,
    ),
  );

  res.json({ ...Object.fromEntries(entries), amexByOwner });
});

// ─── POST /api/admin/process ─────────────────────────────────────────────────

type StagedStatus = "pending" | "processed" | "skipped" | "errored";

importRouter.post("/process", async (_req, res) => {
  let processed = 0;
  let skipped = 0;
  let errored = 0;

  const categoryRules = await db.categoryRule.findMany({ include: { category: true } });

  // ── Monzo ──────────────────────────────────────────────────────────────────
  // The retail (debit) account's ID is the "primary" one stored on the credential.
  // Any other account synced (currently just Flex) is everything else — treated
  // as its own bank/card so it gets its own externalId namespace and rule scope.
  const pendingMonzo = await db.monzoApiTransaction.findMany({ where: { status: "pending" } });
  const monzoCredential = await db.monzoCredential.findFirst({ select: { accountId: true } });

  for (const row of pendingMonzo) {
    const isFlex = !!monzoCredential?.accountId && row.accountId !== monzoCredential.accountId;
    const bank = isFlex ? "flex" : "monzo";
    const type = row.amountPence >= 0 ? "Income" : "Expense";
    const amount = Math.abs(row.amountPence) / 100;
    const name = row.merchantName ?? row.description;
    const categoryName = resolveCategory(row.monzoCategory, name);
    const category =
      resolveRuleCategory(categoryRules, bank, name) ?? (await findCategory(categoryName));
    const owner = resolveOwner(categoryName, name, "Alex");
    const externalId = `${bank}:${row.monzoId}`;
    const exists = await db.transaction.findUnique({ where: { externalId }, select: { id: true } });
    if (!exists)
      await db.transaction.create({
        data: {
          description: name,
          amount,
          type,
          date: row.created,
          categoryId: category.id,
          externalId,
          owner,
        },
      });
    await db.monzoApiTransaction.update({ where: { id: row.id }, data: { status: "processed" } });
    processed++;
  }

  // ── Amex ───────────────────────────────────────────────────────────────────
  const pendingAmex = await db.amexTransaction.findMany({ where: { status: "pending" } });

  for (const row of pendingAmex) {
    const amountNum = parseFloat(row.amount.replace(/,/g, ""));
    let next: StagedStatus;
    if (isNaN(amountNum)) {
      next = "errored";
      errored++;
    } else {
      const categoryName = resolveCategoryByMerchant(row.description);
      const category =
        resolveRuleCategory(categoryRules, "amex", row.description) ??
        (await findCategory(categoryName));
      const amexExtId = `amex:${row.transactionId}`;
      const amexExists = await db.transaction.findUnique({
        where: { externalId: amexExtId },
        select: { id: true },
      });
      if (!amexExists)
        await db.transaction.create({
          data: {
            description: row.description,
            amount: amountNum,
            type: row.isCredit ? "Income" : "Expense",
            date: new Date(row.transactionDate),
            categoryId: category.id,
            externalId: amexExtId,
            owner: row.owner as "Alex" | "Casey" | "Joint",
          },
        });
      next = "processed";
      processed++;
    }
    await db.amexTransaction.update({
      where: { transactionId: row.transactionId },
      data: { status: next },
    });
  }

  // ── Barclays ───────────────────────────────────────────────────────────────
  const pendingBarclays = await db.barclaysTransaction.findMany({ where: { status: "pending" } });

  for (const row of pendingBarclays) {
    let next: StagedStatus;
    if (row.isCredit) {
      next = "skipped";
      skipped++;
    } else {
      const amountNum = parseFloat(row.amount.replace(/,/g, ""));
      if (isNaN(amountNum)) {
        next = "errored";
        errored++;
      } else {
        const categoryName = resolveCategoryByMerchant(row.description);
        const category =
          resolveRuleCategory(categoryRules, "barclays", row.description) ??
          (await findCategory(categoryName));
        const barclaysExtId = `barclays:${row.transactionId ?? row.id}`;
        const barclaysExists = await db.transaction.findUnique({
          where: { externalId: barclaysExtId },
          select: { id: true },
        });
        if (!barclaysExists)
          await db.transaction.create({
            data: {
              description: row.description,
              amount: amountNum,
              type: "Expense",
              date: new Date(row.date),
              categoryId: category.id,
              externalId: barclaysExtId,
              owner: row.owner as "Alex" | "Casey" | "Joint",
            },
          });
        next = "processed";
        processed++;
      }
    }
    await db.barclaysTransaction.update({ where: { id: row.id }, data: { status: next } });
  }

  // ── Santander ──────────────────────────────────────────────────────────────
  const pendingSantander = await db.santanderTransaction.findMany({ where: { status: "pending" } });

  for (const row of pendingSantander) {
    const isIncome = row.moneyIn !== null;
    const amountStr = row.moneyIn ?? row.moneyOut ?? "";
    const amountNum = parseFloat(amountStr.replace(/,/g, ""));
    let next: StagedStatus;
    if (isNaN(amountNum)) {
      next = "errored";
      errored++;
    } else if (amountNum === 0) {
      next = "skipped";
      skipped++;
    } else {
      const categoryName = resolveCategoryByMerchant(row.description);
      const category =
        resolveRuleCategory(categoryRules, "santander", row.description) ??
        (await findCategory(categoryName));
      const santanderExtId = `santander:${row.transactionId ?? row.id}`;
      const santanderExists = await db.transaction.findUnique({
        where: { externalId: santanderExtId },
        select: { id: true },
      });
      if (!santanderExists)
        await db.transaction.create({
          data: {
            description: row.description,
            amount: amountNum,
            type: isIncome ? "Income" : "Expense",
            date: new Date(row.date),
            categoryId: category.id,
            externalId: santanderExtId,
            owner: row.owner as "Alex" | "Casey" | "Joint",
          },
        });
      next = "processed";
      processed++;
    }
    await db.santanderTransaction.update({ where: { id: row.id }, data: { status: next } });
  }

  // ── HSBC ───────────────────────────────────────────────────────────────────
  const pendingHsbc = await db.hsbcTransaction.findMany({ where: { status: "pending" } });

  for (const row of pendingHsbc) {
    const isIncome = row.moneyIn !== null;
    const amountStr = row.moneyIn ?? row.moneyOut ?? "";
    const amountNum = parseFloat(amountStr.replace(/,/g, ""));
    const parsedDate = new Date(row.date);
    let next: StagedStatus;
    // A row with an unparseable amount or date (e.g. junk from a non-HSBC PDF that slipped in
    // before the upload guard) is marked errored and skipped — never let one bad row throw and
    // abort the whole /process run.
    if (isNaN(amountNum) || isNaN(parsedDate.getTime())) {
      next = "errored";
      errored++;
    } else if (amountNum === 0) {
      next = "skipped";
      skipped++;
    } else {
      const categoryName = resolveCategoryByMerchant(row.description);
      const category =
        resolveRuleCategory(categoryRules, "hsbc", row.description) ??
        (await findCategory(categoryName));
      const hsbcExtId = `hsbc:${row.transactionId}`;
      const hsbcExists = await db.transaction.findUnique({
        where: { externalId: hsbcExtId },
        select: { id: true },
      });
      if (!hsbcExists)
        await db.transaction.create({
          data: {
            description: row.description,
            amount: amountNum,
            type: isIncome ? "Income" : "Expense",
            date: parsedDate,
            categoryId: category.id,
            externalId: hsbcExtId,
            owner: row.owner as "Alex" | "Casey" | "Joint",
          },
        });
      next = "processed";
      processed++;
    }
    await db.hsbcTransaction.update({ where: { id: row.id }, data: { status: next } });
  }

  // ── SoFi (USD → GBP) ───────────────────────────────────────────────────────
  const pendingSofi = await db.sofiTransaction.findMany({ where: { status: "pending" } });

  for (const row of pendingSofi) {
    const usdAmount = parseFloat(row.amount.replace(/,/g, ""));
    let next: StagedStatus;
    if (/^(From|To)\s+(Savings|Checking)/i.test(row.description)) {
      next = "skipped";
      skipped++;
    } else if (isNaN(usdAmount)) {
      next = "errored";
      errored++;
    } else if (usdAmount === 0) {
      next = "skipped";
      skipped++;
    } else {
      const categoryName = resolveCategoryByMerchant(row.description);
      const category =
        resolveRuleCategory(categoryRules, "sofi", row.description) ??
        (await findCategory(categoryName));
      const sofiExtId = `sofi:${row.transactionId}`;
      const sofiExists = await db.transaction.findUnique({
        where: { externalId: sofiExtId },
        select: { id: true },
      });
      if (sofiExists) {
        next = "processed";
        processed++;
      } else {
        const txDate = new Date(row.date);
        const { amount: gbpAmount } = await convertWithFallback(
          usdAmount,
          "USD",
          "GBP",
          txDate,
          sofiExtId,
        );
        await db.transaction.create({
          data: {
            description: row.description,
            amount: gbpAmount,
            originalAmount: usdAmount,
            originalCurrency: "USD",
            type: row.isCredit ? "Income" : "Expense",
            date: txDate,
            categoryId: category.id,
            externalId: sofiExtId,
            owner: row.owner as "Alex" | "Casey" | "Joint",
          },
        });
        next = "processed";
        processed++;
      }
    }
    await db.sofiTransaction.update({ where: { id: row.id }, data: { status: next } });
  }

  // ── Chase (USD → GBP) ──────────────────────────────────────────────────────
  const pendingChase = await db.chaseTransaction.findMany({ where: { status: "pending" } });

  for (const row of pendingChase) {
    const usdAmount = parseFloat(row.amount.replace(/,/g, ""));
    let next: StagedStatus;
    if (isNaN(usdAmount)) {
      next = "errored";
      errored++;
    } else if (usdAmount === 0) {
      next = "skipped";
      skipped++;
    } else {
      const categoryName = resolveCategoryByMerchant(row.description);
      const category =
        resolveRuleCategory(categoryRules, "chase", row.description) ??
        (await findCategory(categoryName));
      const chaseExtId = `chase:${row.transactionId}`;
      const chaseExists = await db.transaction.findUnique({
        where: { externalId: chaseExtId },
        select: { id: true },
      });
      if (chaseExists) {
        next = "processed";
        processed++;
      } else {
        const txDate = new Date(row.date);
        const { amount: gbpAmount } = await convertWithFallback(
          usdAmount,
          "USD",
          "GBP",
          txDate,
          chaseExtId,
        );
        await db.transaction.create({
          data: {
            description: row.description,
            amount: gbpAmount,
            originalAmount: usdAmount,
            originalCurrency: "USD",
            type: row.isCredit ? "Income" : "Expense",
            date: txDate,
            categoryId: category.id,
            externalId: chaseExtId,
            owner: row.owner as "Alex" | "Casey" | "Joint",
          },
        });
        next = "processed";
        processed++;
      }
    }
    await db.chaseTransaction.update({ where: { id: row.id }, data: { status: next } });
  }

  // ── Plaid (Santander) ─────────────────────────────────────────────────────
  const pendingPlaid = await db.plaidTransaction.findMany({ where: { status: "pending" } });

  for (const row of pendingPlaid) {
    // Plaid: positive amount = expense, negative = income (counterintuitive)
    let next: StagedStatus;
    if (row.amount === 0) {
      next = "skipped";
      skipped++;
    } else {
      const categoryName = resolveCategoryByMerchant(row.description);
      const category = await findCategory(categoryName);
      const extId = `santander-plaid:${row.transactionId}`;
      const exists = await db.transaction.findUnique({
        where: { externalId: extId },
        select: { id: true },
      });
      if (!exists)
        await db.transaction.create({
          data: {
            description: row.description,
            amount: Math.abs(row.amount),
            type: row.amount > 0 ? "Expense" : "Income",
            date: new Date(row.date),
            categoryId: category.id,
            externalId: extId,
            owner: row.owner as "Alex" | "Casey" | "Joint",
          },
        });
      next = "processed";
      processed++;
    }
    await db.plaidTransaction.update({ where: { id: row.id }, data: { status: next } });
  }

  res.json({ processed, skipped, errored });
});

// ─── POST /api/admin/backfill/usd-gbp ────────────────────────────────────────
// One-shot backfill for SoFi/Chase transactions that were imported before FX
// conversion was added. Idempotent: rows with originalAmount set are skipped.
// Pass ?dryRun=true to preview.
importRouter.post("/backfill/usd-gbp", async (req, res) => {
  const dryRun = req.query.dryRun === "true";

  const rows = await db.transaction.findMany({
    where: {
      originalAmount: null,
      OR: [{ externalId: { startsWith: "sofi:" } }, { externalId: { startsWith: "chase:" } }],
    },
    select: { id: true, amount: true, date: true, externalId: true },
  });

  let converted = 0;
  let errored = 0;
  const errors: string[] = [];

  for (const row of rows) {
    try {
      const usd = Number(row.amount);
      const { amount: gbp } = await convertWithFallback(
        usd,
        "USD",
        "GBP",
        row.date,
        row.externalId ?? row.id,
      );
      if (!dryRun) {
        await db.transaction.update({
          where: { id: row.id },
          data: { amount: gbp, originalAmount: usd, originalCurrency: "USD" },
        });
      }
      converted++;
    } catch (err) {
      errored++;
      errors.push(
        `${row.externalId ?? row.id}: ${err instanceof Error ? err.message : String(err)}`,
      );
    }
  }

  res.json({ candidates: rows.length, converted, errored, errors: errors.slice(0, 20), dryRun });
});

import { test, expect, describe } from "bun:test";
import { readFileSync } from "fs";
import { resolve } from "path";
import pdfParse from "pdf-parse";
import {
  parseAmexPdf,
  amexPageRender,
  readAmexNewSpendTotal,
  readAmexAccountSummary,
  reconcileAmex,
  assignAmexIds,
  type AmexRow,
} from "./import.js";

// Integration tests against a REAL Amex statement PDF.
//
// Page 1 prints an Account Summary that boxes the whole statement in:
//
//   Previous Closing Balance − New Credits + New Debits = Closing Balance
//        £2,805.95              £2,815.95     £2,739.16     £2,729.16
//
// Those are the authoritative figures the parse must reconcile to:
//
//   Σ amount where !isCredit === New Debits
//   Σ amount where  isCredit === New Credits
//
// A parse that drops a row, takes a foreign-currency amount instead of the £
// amount, slides the amount column against the description column, or skips the
// OTHER ACCOUNT TRANSACTIONS section does not satisfy both.

const NAME = "2026-07-24-amex.pdf";

// Every real Amex statement we have. Each must reconcile against its own printed
// Account Summary — that's the whole safety net, so it runs over all of them.
const STATEMENTS = [
  "2026-01-24-amex.pdf",
  "2026-02-24-amex.pdf",
  "2026-03-24-amex.pdf",
  "2026-07-24-amex.pdf",
];

function pence(s: string): number {
  return Math.round(parseFloat(s.replace(/,/g, "")) * 100);
}

const cache = new Map<string, { text: string; rows: AmexRow[] }>();

async function load(name: string): Promise<{ text: string; rows: AmexRow[] }> {
  const hit = cache.get(name);
  if (hit) return hit;
  const buf = readFileSync(resolve(import.meta.dir, "../../../e2e/resources/statements", name));
  const text = (await pdfParse(buf, { pagerender: amexPageRender })).text;
  const parsed = { text, rows: parseAmexPdf(text) };
  cache.set(name, parsed);
  return parsed;
}

async function loadStatement(): Promise<{ text: string; rows: AmexRow[] }> {
  return load(NAME);
}

describe("every statement reconciles to its own Account Summary", () => {
  for (const name of STATEMENTS) {
    test(name, async () => {
      const { text, rows } = await load(name);
      const s = readAmexAccountSummary(text);
      expect(s).not.toBeNull();
      // The summary box's own arithmetic — proves we read the right four figures.
      expect(pence(s!.previousClosingBalance) - pence(s!.newCredits) + pence(s!.newDebits)).toBe(
        pence(s!.closingBalance),
      );
      const debits = rows.filter((r) => !r.isCredit).reduce((a, r) => a + pence(r.amount), 0);
      const credits = rows.filter((r) => r.isCredit).reduce((a, r) => a + pence(r.amount), 0);
      expect(debits).toBe(pence(s!.newDebits));
      expect(credits).toBe(pence(s!.newCredits));
      expect(reconcileAmex(rows, text)).toBeNull();
    });
  }

  test("no statement produces a colliding or malformed row", async () => {
    for (const name of STATEMENTS) {
      const { rows } = await load(name);
      const ids = assignAmexIds(rows, "Alex").map((r) => r.transactionId);
      expect(new Set(ids).size).toBe(rows.length);
      for (const r of rows) {
        // A real calendar date — the old parser could emit "2026-01-57" by eating
        // a digit off the front of the merchant name.
        expect(r.transactionDate).toMatch(/^\d{4}-\d{2}-\d{2}$/);
        expect(r.processDate).toMatch(/^\d{4}-\d{2}-\d{2}$/);
        expect(Number.isNaN(Date.parse(r.processDate))).toBe(false);
        expect(r.description.length).toBeGreaterThan(0);
      }
    }
  });
});

describe("merchants whose name starts with a digit", () => {
  // January has three. The old parser's regex "(Mon)(\d{1,2})(.+)" was greedy, so
  // the leading digit of the merchant was swallowed into the process day:
  //   "Jan" "5" + "73 Upper Street" → 5 Jan became 57 Jan, description "3 Upper…"
  // Column positions make this unambiguous — the date and the description are
  // different cells, so no regex has to guess where one ends.
  test.each([
    ["1 TORTILLA ISLINGTON", "2025-12-31", "2026-01-01", "32.40"],
    ["4226 -", "2026-01-01", "2026-01-01", "88.00"],
    ["73 Upper Street", "2026-01-04", "2026-01-05", "18.00"],
  ])("%s keeps its leading digit and a valid process date", async (needle, tx, proc, amount) => {
    const { rows } = await load("2026-01-24-amex.pdf");
    const row = rows.find((r) => r.description.startsWith(needle));
    expect(row).toBeDefined();
    expect(row!.transactionDate).toBe(tx);
    expect(row!.processDate).toBe(proc);
    expect(row!.amount).toBe(amount);
  });
});

describe("page 1 Account Summary", () => {
  test("reads all four figures off the summary box", async () => {
    const { text } = await loadStatement();
    expect(readAmexAccountSummary(text)).toEqual({
      previousClosingBalance: "2,805.95",
      newCredits: "2,815.95",
      newDebits: "2,739.16",
      closingBalance: "2,729.16",
    });
  });

  test("the summary's own arithmetic holds, so we read the right four boxes", async () => {
    const { text } = await loadStatement();
    const s = readAmexAccountSummary(text)!;
    expect(pence(s.previousClosingBalance) - pence(s.newCredits) + pence(s.newDebits)).toBe(
      pence(s.closingBalance),
    );
  });
});

describe("Amex statement parse (real PDF)", () => {
  test("Σ debits === New Debits (£2,739.16)", async () => {
    const { text, rows } = await loadStatement();
    const { newDebits } = readAmexAccountSummary(text)!;
    const debits = rows.filter((r) => !r.isCredit).reduce((a, r) => a + pence(r.amount), 0);
    expect(debits).toBe(pence(newDebits));
    // Same figure, printed a second time at the foot of the transaction table.
    expect(readAmexNewSpendTotal(text)).toBe(newDebits);
  });

  test("Σ credits === New Credits (£2,815.95)", async () => {
    const { text, rows } = await loadStatement();
    const { newCredits } = readAmexAccountSummary(text)!;
    const credits = rows.filter((r) => r.isCredit).reduce((a, r) => a + pence(r.amount), 0);
    // £2,805.95 card payment + 2 × £5.00 Deliveroo Gold benefit. Skipping the
    // OTHER ACCOUNT TRANSACTIONS section leaves this £10.00 short.
    expect(credits).toBe(pence(newCredits));
  });

  test("parses both sections: 40 new-spend rows + 2 other-account rows", async () => {
    const { rows } = await loadStatement();
    expect(rows).toHaveLength(42);
  });

  test("statement date comes off the page header", async () => {
    const { rows } = await loadStatement();
    expect(new Set(rows.map((r) => r.statementDate))).toEqual(new Set(["24/07/26"]));
  });
});

describe("OTHER ACCOUNT TRANSACTIONS section", () => {
  // Printed below "Total new spend transactions", these aren't card spend — they
  // are statement credits (Deliveroo Gold benefit, £5.00 each) that still count
  // towards New Credits and so must be imported.
  test("both £5.00 Deliveroo Gold credits are parsed", async () => {
    const { rows } = await loadStatement();
    const benefits = rows.filter((r) => r.isCredit && r.amount === "5.00");
    expect(benefits).toHaveLength(2);
    expect(benefits.every((r) => /DELIVEROO/i.test(r.description))).toBe(true);
    expect(benefits.map((r) => r.transactionDate)).toEqual(["2026-07-05", "2026-07-11"]);
  });

  test("they are credits, not debits — and the £5.00s don't collide with real Deliveroo spend", async () => {
    const { rows } = await loadStatement();
    const deliveroo = rows.filter((r) => /DELIVEROO/i.test(r.description));
    expect(deliveroo).toHaveLength(4);
    expect(deliveroo.filter((r) => r.isCredit).map((r) => r.amount)).toEqual(["5.00", "5.00"]);
    expect(
      deliveroo
        .filter((r) => !r.isCredit)
        .map((r) => r.amount)
        .sort(),
    ).toEqual(["44.40", "49.99"]);
    // All four hash apart, so none is dropped as a duplicate of another.
    expect(new Set(assignAmexIds(deliveroo, "Alex").map((r) => r.transactionId)).size).toBe(4);
  });

  test("the card payment is still the only credit in the new-spend section", async () => {
    const { rows } = await loadStatement();
    const big = rows.filter((r) => r.isCredit && r.amount !== "5.00");
    expect(big).toHaveLength(1);
    expect(big[0].description).toContain("PAYMENT RECEIVED");
    expect(big[0].amount).toBe("2,805.95");
  });

  test("the section's own total line is not itself imported as a transaction", async () => {
    const { rows } = await loadStatement();
    expect(rows.some((r) => /^Total/i.test(r.description))).toBe(false);
    expect(rows.some((r) => r.amount === "10.00")).toBe(false);
  });
});

// ── The defects this statement is catching ───────────────────────────────────

describe("amount column alignment", () => {
  // The reported bug: the £5.35 belonging to the 9 Jul TFL TRAVEL CHARGE landed
  // on the 10 Jul LIME*RIDE row instead. Cause: a foreign-spend amount (USD 2.57,
  // in the Foreign Spend column) was scooped into the page's amount list, sliding
  // every amount on page 3 one row down against its description.
  test("9 Jul TFL TRAVEL CHARGE keeps its £5.35", async () => {
    const { rows } = await loadStatement();
    const tfl = rows.find((r) => r.transactionDate === "2026-07-09" && /^TFL/.test(r.description));
    expect(tfl?.amount).toBe("5.35");
    expect(tfl?.processDate).toBe("2026-07-10");
  });

  test("no LIME*RIDE row is ever £5.35", async () => {
    const { rows } = await loadStatement();
    const lime = rows.filter((r) => /^LIME/.test(r.description));
    expect(lime.length).toBeGreaterThan(0);
    expect(lime.map((r) => r.amount)).not.toContain("5.35");
  });

  test("foreign-currency row takes the £ amount, not the USD amount", async () => {
    const { rows } = await loadStatement();
    // 30 Jun RAILWAY SAN FRANCISCO: USD 2.57 @1.3247 → £2.00 charged. The GBP
    // figure is what hits the balance.
    const railway = rows.find((r) => /^RAILWAY/.test(r.description));
    expect(railway?.amount).toBe("2.00");
    expect(railway?.foreignAmount).toBe("2.57");
    expect(railway?.foreignCurrency).toBe("UNITED STATES DOLLAR");
  });

  test("first and last new-spend rows land on the right amounts", async () => {
    const { rows } = await loadStatement();
    expect(rows[1]).toMatchObject({
      transactionDate: "2026-06-24",
      description: expect.stringContaining("TFL TRAVEL CHARGE"),
      amount: "1.75",
    });
    expect(rows[39]).toMatchObject({
      transactionDate: "2026-07-10",
      description: expect.stringContaining("GOOGLE*YOUTUBE"),
      amount: "12.99",
    });
  });
});

describe("upload guard", () => {
  // reconcileAmex is what the route calls before staging anything; a non-null
  // return is a 422 and no rows are written.
  test("the real statement reconciles clean", async () => {
    const { text, rows } = await loadStatement();
    expect(reconcileAmex(rows, text)).toBeNull();
  });

  test("a dropped row is rejected, not staged", async () => {
    const { text, rows } = await loadStatement();
    expect(reconcileAmex(rows.slice(1), text)).toMatch(/didn't parse cleanly/);
  });

  test("skipping OTHER ACCOUNT TRANSACTIONS is rejected on the credit side", async () => {
    const { text, rows } = await loadStatement();
    const newSpendOnly = rows.filter((r) => r.amount !== "5.00");
    expect(reconcileAmex(newSpendOnly, text)).toMatch(/credits total £2,805\.95/);
  });

  test("a statement with no Account Summary is rejected rather than trusted", async () => {
    const { rows } = await loadStatement();
    expect(reconcileAmex(rows, "some other PDF text")).toMatch(/Account Summary/);
  });
});

describe("identical rows within one statement", () => {
  // 10 Jul has two LIME*RIDE KHJA £1.70 hires with identical dates, description
  // and amount. Both are real charges, so both must import — but re-uploading the
  // same statement must recognise both as already-seen.
  test("both 10 Jul LIME*RIDE £1.70 hires survive as distinct rows", async () => {
    const { rows } = await loadStatement();
    const twins = rows.filter(
      (r) => r.transactionDate === "2026-07-10" && /^LIME/.test(r.description),
    );
    expect(twins).toHaveLength(2);
    expect(twins.every((r) => r.amount === "1.70")).toBe(true);

    const ids = assignAmexIds(rows, "Alex").map((r) => r.transactionId);
    expect(new Set(ids).size).toBe(rows.length);
    // The second copy is suffixed rather than dropped.
    expect(ids.filter((id) => id.includes("-"))).toHaveLength(1);
  });

  test("ids are stable across re-parses, so a re-upload is fully detected", async () => {
    const a = assignAmexIds((await loadStatement()).rows, "Alex").map((r) => r.transactionId);
    const b = assignAmexIds((await loadStatement()).rows, "Alex").map((r) => r.transactionId);
    expect(new Set(b)).toEqual(new Set(a));
  });

  test("the same charge under a different owner is a different row", async () => {
    // Amex is shared between owners. Without the owner in the key, Alex and Casey
    // both being charged £1.75 by TFL on the same day would silently drop one.
    const { rows } = await loadStatement();
    const alex = assignAmexIds(rows, "Alex").map((r) => r.transactionId);
    const casey = assignAmexIds(rows, "Casey").map((r) => r.transactionId);
    expect(casey).toHaveLength(alex.length);
    expect(alex.filter((id) => casey.includes(id))).toEqual([]);
  });

  test("id assignment does not depend on the order rows arrive in", async () => {
    const { rows } = await loadStatement();
    const forward = assignAmexIds(rows, "Alex").map((r) => r.transactionId);
    const reversed = assignAmexIds([...rows].reverse(), "Alex").map((r) => r.transactionId);
    expect(new Set(reversed)).toEqual(new Set(forward));
  });
});

import { test, expect, describe } from "bun:test";
import { readFileSync } from "fs";
import { resolve } from "path";
import pdfParse from "pdf-parse";
import { parseHsbcPdf, hsbcPageRender, type HsbcRow } from "./import.js";

// Integration tests against REAL HSBC statement PDFs (Casey's Premier account).
// Each statement's balance column gives us authoritative figures the parse must
// reconcile to:
//
//   opening + Σ moneyIn − Σ moneyOut === closing
//
// where opening = first "BALANCE BROUGHT FORWARD" amount and closing = last
// "BALANCE CARRIED FORWARD" amount. A correct parse of every page satisfies it;
// a parse that drops rows, mis-signs a row, or takes a foreign amount does not.

function statementPath(name: string): string {
  return resolve(import.meta.dir, "../../../e2e/resources/statements", name);
}

function pence(s: string): number {
  return Math.round(parseFloat(s.replace(/,/g, "")) * 100);
}

function readStatementBalances(text: string): { opening: number; closing: number } {
  const brought = [...text.matchAll(/BALANCE BROUGHT FORWARD\s+([\d,]+\.\d{2})/g)];
  const carried = [...text.matchAll(/BALANCE CARRIED FORWARD\s+([\d,]+\.\d{2})/g)];
  return { opening: pence(brought[0][1]), closing: pence(carried.at(-1)![1]) };
}

async function loadStatement(
  name: string,
): Promise<{ text: string; rows: HsbcRow[]; opening: number; closing: number }> {
  const buf = readFileSync(statementPath(name));
  const text = (await pdfParse(buf, { pagerender: hsbcPageRender })).text;
  const rows = parseHsbcPdf(text);
  return { text, rows, ...readStatementBalances(text) };
}

function sums(rows: HsbcRow[]): { sumIn: number; sumOut: number } {
  return {
    sumIn: rows.reduce((a, r) => a + (r.moneyIn ? pence(r.moneyIn) : 0), 0),
    sumOut: rows.reduce((a, r) => a + (r.moneyOut ? pence(r.moneyOut) : 0), 0),
  };
}

// ── The core invariant, run over every real statement we have ────────────────
const STATEMENTS = [
  "2026-04-09-hsbc-statement.pdf", // 10 Mar → 9 Apr
  "2026-05-09-hsbc-statement.pdf", // 10 Apr → 9 May
];

describe("HSBC statement reconciliation (real PDFs)", () => {
  for (const name of STATEMENTS) {
    test(`${name}: opening + Σin − Σout === closing`, async () => {
      const { rows, opening, closing } = await loadStatement(name);
      const { sumIn, sumOut } = sums(rows);
      expect(opening + sumIn - sumOut).toBe(closing);
    });
  }
});

// ── Statement-specific defects the reconciliation is catching ────────────────

describe("10 Mar → 9 Apr statement", () => {
  const NAME = "2026-04-09-hsbc-statement.pdf";

  test("captures every transaction date, through 08 Apr on page 2", async () => {
    const { rows } = await loadStatement(NAME);
    const maxDate = rows.reduce((m, r) => (r.date > m ? r.date : m), "");
    // Statement runs to 09 Apr; last transaction is 08 Apr. A parser that stops
    // at the page-1 BALANCE BROUGHT FORWARD wrongly reports 01 Apr.
    expect(maxDate).toBe("2026-04-08");
  });

  test("foreign-currency row uses the £ amount, not the USD amount", async () => {
    const { rows } = await loadStatement(NAME);
    // 01 Apr WELLS FARGO: USD 164.73 @1.3169 → £125.08 paid out. The GBP figure
    // is what hits the balance, not the 164.73 USD.
    const wells = rows.find((r) => /WELLS FARGO/i.test(r.description));
    expect(wells?.moneyOut).toBe("125.08");
  });
});

describe("10 Apr → 9 May statement", () => {
  const NAME = "2026-05-09-hsbc-statement.pdf";

  test("incoming BP payments are money in, not money out", async () => {
    const { rows } = await loadStatement(NAME);
    // 13 Apr: SIMON M +140 and WHITE A +140 arrive as BP (not CR). Balance rises
    // 4,936.84 → 5,216.84, so both are money IN. Mis-signing them as OUT throws
    // the reconciliation off by 2 × 2 × 140 = £560.
    const simon = rows.find((r) => /SIMON M/i.test(r.description));
    const white = rows.find((r) => /WHITE A/i.test(r.description));
    expect(simon?.moneyIn).toBe("140.00");
    expect(simon?.moneyOut).toBeNull();
    expect(white?.moneyIn).toBe("140.00");
    expect(white?.moneyOut).toBeNull();
  });
});

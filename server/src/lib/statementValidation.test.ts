import { test, expect, describe } from "bun:test";
import { hsbcStatementTextSchema } from "./statementValidation.js";

// Representative scraps of pdf-parse text from each statement type.
const AMEX_TEXT = `American Express
Mr A MACKINTOSH 31/05/26
Membership Rewards
Total new spend transactions
Jan5Jan5SAINSBURYS LONDON 19.00`;

const HSBC_TEXT = `HSBC UK Bank plc
Your HSBC Bank Statement
1 May 2026 to 31 May 2026
BALANCE BROUGHT FORWARD
02 May 26 DD SKY TV 51.00 11,867.05`;

describe("hsbcStatementTextSchema", () => {
  test("rejects an Amex statement uploaded to the HSBC endpoint", () => {
    const result = hsbcStatementTextSchema.safeParse(AMEX_TEXT);
    expect(result.success).toBe(false);
  });

  test("accepts a genuine HSBC statement", () => {
    const result = hsbcStatementTextSchema.safeParse(HSBC_TEXT);
    expect(result.success).toBe(true);
  });

  test("rejects unrelated text with no HSBC marker", () => {
    expect(hsbcStatementTextSchema.safeParse("just some random pdf text").success).toBe(false);
  });
});

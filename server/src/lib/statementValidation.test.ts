import { test, expect, describe } from "bun:test";
import { statementSchemas, hsbcStatementTextSchema, type BankKey } from "./statementValidation.js";

// Representative scraps of pdf-parse text — each carries its issuing bank's name.
const SAMPLES: Record<BankKey, string> = {
  amex: "American Express\nMr A MACKINTOSH 31/05/26\nMembership Rewards\nTotal new spend transactions",
  barclays: "Barclaycard\nBarclays Bank UK PLC\nHow you've used your card\n05 May SAINSBURYS £19.00",
  santander: "Santander\nYour transactions\nBalance brought forward 1,000.00",
  hsbc: "HSBC UK Bank plc\n1 May 2026 to 31 May 2026\nBALANCE BROUGHT FORWARD",
  chase: "Chase\nwww.chase.com\nJPMorgan Chase Bank, N.A.\n01/12 Payment Thank You -100.00",
  sofi: "SoFi Bank, N.A.\nChecking Account - 1234\nTransaction ID: abc",
};

const BANKS = Object.keys(SAMPLES) as BankKey[];

describe("statement guards", () => {
  for (const bank of BANKS) {
    test(`${bank}: accepts a genuine ${bank} statement`, () => {
      expect(statementSchemas[bank].safeParse(SAMPLES[bank]).success).toBe(true);
    });

    test(`${bank}: rejects every other bank's statement`, () => {
      for (const other of BANKS) {
        if (other === bank) continue;
        const result = statementSchemas[bank].safeParse(SAMPLES[other]);
        expect(result.success).toBe(false);
      }
    });
  }

  test("rejects unrelated text with no bank marker", () => {
    expect(statementSchemas.hsbc.safeParse("just some random pdf text").success).toBe(false);
  });

  test("names the likely bank in the error message", () => {
    const result = statementSchemas.hsbc.safeParse(SAMPLES.amex);
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues[0].message).toContain("American Express");
    }
  });

  test("back-compat hsbcStatementTextSchema still works", () => {
    expect(hsbcStatementTextSchema.safeParse(SAMPLES.hsbc).success).toBe(true);
    expect(hsbcStatementTextSchema.safeParse(SAMPLES.amex).success).toBe(false);
  });
});

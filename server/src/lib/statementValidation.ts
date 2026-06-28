import { z } from "zod";

// Statement upload guards — the JS/React equivalent of C# FluentValidation is Zod.
// Every bank endpoint accepts any PDF, so it's easy to upload one bank's statement to
// another's card. A statement's extracted text always contains the issuing bank's name
// (header / legal footer), so we require that marker before parsing — turning a silent
// bad import into a clear 422. The positive "must contain own bank name" check is the
// decider; the other-bank match only makes the error message friendlier.

export type BankKey = "amex" | "barclays" | "santander" | "hsbc" | "chase" | "sofi";

interface BankMarker {
  bank: BankKey;
  label: string;
  pattern: RegExp;
}

const BANK_MARKERS: BankMarker[] = [
  { bank: "amex", label: "American Express", pattern: /American Express|Membership Rewards/i },
  { bank: "barclays", label: "Barclays", pattern: /\bBarclay/i }, // Barclays / Barclaycard
  { bank: "santander", label: "Santander", pattern: /\bSantander\b/i },
  { bank: "hsbc", label: "HSBC", pattern: /\bHSBC\b/i },
  { bank: "chase", label: "Chase", pattern: /\bChase\b|JPMorgan/i }, // \b avoids "purchase"
  { bank: "sofi", label: "SoFi", pattern: /\bSoFi\b/i },
];

function statementSchemaFor(self: BankMarker) {
  return z.string().superRefine((t, ctx) => {
    if (self.pattern.test(t)) return;
    const other = BANK_MARKERS.find((b) => b !== self && b.pattern.test(t));
    ctx.addIssue({
      code: "custom",
      message: other
        ? `This looks like a ${other.label} statement, not ${self.label}. Upload it under the ${other.label} card instead.`
        : `This doesn't look like a ${self.label} statement — no "${self.label}" marker found. Did you upload the right bank's file?`,
    });
  });
}

export const statementSchemas = Object.fromEntries(
  BANK_MARKERS.map((m) => [m.bank, statementSchemaFor(m)]),
) as Record<BankKey, ReturnType<typeof statementSchemaFor>>;

// Back-compat alias (HSBC was the first guard added).
export const hsbcStatementTextSchema = statementSchemas.hsbc;

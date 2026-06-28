import { z } from "zod";

// Statement upload guards — the JS/React equivalent of C# FluentValidation is Zod.
// Each bank endpoint accepts any PDF, so it's easy to upload (say) an Amex statement
// to the HSBC card. These schemas validate the *extracted text* looks like the right
// bank before we parse it, turning a silent bad import into a clear 422.

/** Extracted PDF text must look like an HSBC statement (and not another bank's). */
export const hsbcStatementTextSchema = z
  .string()
  .refine((t) => /\bHSBC\b/i.test(t), {
    message:
      "This doesn't look like an HSBC statement — no “HSBC” found in the document. Did you upload the right bank's file?",
  })
  .refine((t) => !/American Express|Membership Rewards|Cardmember/i.test(t), {
    message:
      "This looks like an American Express statement, not HSBC. Upload it under the Amex card instead.",
  });

/**
 * Generates e2e/fixtures/barclays-dedup.pdf — a minimal Barclays credit card
 * statement PDF that the real parseBarclaysPdf() parser in server/src/routes/import.ts
 * will correctly parse.
 *
 * Run once:
 *   node e2e/fixtures/generate-barclays-fixture.mjs
 *
 * The generated PDF is committed so tests don't depend on pdfkit at runtime.
 *
 * Layout chosen to satisfy every parser regex:
 *
 *   BARCLAYS_ISSUED_RE:  /issued on (\d+)\s+(\w+)\s+(\d{4})/
 *   Section header:       line === "How you've used your card"
 *   BARCLAYS_TX_RE:       /^(\d{2})\s+(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)(.+)/
 *   BARCLAYS_AMOUNT_RE:   /£([\d,]+\.\d{2})$/  (at end of same tx line)
 *   BARCLAYS_SENTINEL_RE: /^(Promotional transactions|Your new balance|Interest and charges)/
 *
 * Transactions (7 total, N=7 distinct staged rows after dedup keying):
 *   Lime ride ×4 (same content → base, _1, _2, _3 after within-file dedup)
 *   Coffee ×1
 *   Train ×1
 *   Groceries ×1
 *
 * All descriptions start with E2EDEDUP so cleanup queries are isolated.
 */

import PDFDocument from "pdfkit";
import { createWriteStream } from "fs";
import { fileURLToPath } from "url";
import { dirname, resolve } from "path";

const __dirname = dirname(fileURLToPath(import.meta.url));
const outPath = resolve(__dirname, "barclays-dedup.pdf");

const doc = new PDFDocument({ autoFirstPage: true, margin: 50 });
const stream = createWriteStream(outPath);
doc.pipe(stream);

// Use a monospace-friendly font to keep lines predictable
doc.font("Courier").fontSize(11);

// pdf-parse extracts text line by line. Each doc.text() call = one logical line.
// We need the raw extracted text (after trim + filter empty) to match the regexes.

const lines = [
  // Statement header / metadata
  "Barclaycard Statement",
  "",
  // REQUIRED: issued date footer (BARCLAYS_ISSUED_RE)
  "Page 1 of 1 // issued on 15 April 2026",
  "",
  // REQUIRED: section trigger
  "How you've used your card",
  "",
  // 4x identical Lime ride lines (within-file repeats → _1 _2 _3 suffixes)
  "05 AprE2EDEDUP LIME RIDE£1.75",
  "05 AprE2EDEDUP LIME RIDE£1.75",
  "05 AprE2EDEDUP LIME RIDE£1.75",
  "05 AprE2EDEDUP LIME RIDE£1.75",
  // 3 distinct transactions
  "06 AprE2EDEDUP COFFEE SHOP£3.50",
  "07 AprE2EDEDUP TRAINLINE£42.00",
  "08 AprE2EDEDUP GROCERIES TESCO£21.99",
  "",
  // REQUIRED: sentinel to end spend section (BARCLAYS_SENTINEL_RE)
  "Your new balance is £512.34",
];

for (const line of lines) {
  doc.text(line);
}

doc.end();

stream.on("finish", () => {
  console.log(`Written: ${outPath}`);
  console.log("Transactions in fixture:");
  console.log("  4x E2EDEDUP LIME RIDE £1.75  (→ 4 staged rows: base + _1 _2 _3)");
  console.log("  1x E2EDEDUP COFFEE SHOP £3.50");
  console.log("  1x E2EDEDUP TRAINLINE £42.00");
  console.log("  1x E2EDEDUP GROCERIES TESCO £21.99");
  console.log("  Total N = 7 expected staged rows");
});

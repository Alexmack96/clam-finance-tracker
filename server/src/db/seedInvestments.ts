/**
 * Seeds historical investment portfolio snapshots.
 * Run with: bun run src/db/seedInvestments.ts
 *
 * This script CLEARS all existing investment data before reseeding —
 * safe to re-run when dates or values need correcting.
 *
 * Date convention: always use the last calendar day of the month.
 * Historical dates that were "one or two days off" have been normalised here.
 */
import "dotenv/config";
import { db } from "./client.js";

// All dates are month-end.
// Oct 12 2024 (off-cycle) and Feb 4 2025 (off-cycle) have been dropped.
const DATES = [
  "2024-10-31",
  "2024-11-30",
  "2024-12-31",
  "2025-01-31",
  "2025-02-28",
  "2025-03-31",
  "2025-04-30",
  "2025-05-31",
  "2025-06-30",
  "2025-07-31",
  "2025-08-31",
  "2025-09-30",
  "2025-10-31",
  "2025-11-30",
  "2026-01-31",
  "2026-02-28",
  "2026-03-31",
];

// 17 values per account (one per DATES entry). null = no data for that month.
const ACCOUNTS: {
  name: string;
  category: string;
  rate: number | null;
  sortOrder: number;
  values: (number | null)[];
}[] = [
  {
    name: "Pension",
    category: "pension",
    rate: null,
    sortOrder: 0,
    //         Oct24   Nov24   Dec24   Jan25   Feb25   Mar25   Apr25   May25   Jun25   Jul25   Aug25   Sep25   Oct25   Nov25   Jan26      Feb26   Mar26
    values: [
      43110,
      45850,
      46340,
      48950,
      48639,
      46606,
      43877,
      48106,
      49624,
      53149,
      54622,
      56432,
      59640,
      58711,
      102196,
      null,
      null,
    ],
  },
  {
    name: "Crypto — XRP",
    category: "crypto",
    rate: 50,
    sortOrder: 1,
    values: [
      0, 0, 1000, 1385, 2938, 3085, 2810, 2900, 2710, 4036, 3560, 3443, 3419, 2630, 2145, 1704,
      1709,
    ],
  },
  {
    name: "Crypto — BTC",
    category: "crypto",
    rate: 50,
    sortOrder: 2,
    values: [0, 0, 0, 0, 0, 0, 2815, 3220, 3122, 3552, 3202, 3249, 3468, 2610, 3335, 3650, 3740],
  },
  {
    name: "Equity — VG Global Ex-UK",
    category: "equity",
    rate: 7,
    sortOrder: 3,
    values: [
      0, 0, 0, 0, 3450, 3295, 4390, 4655, 4767, 5014, 5054, 5208, 5433, 5315, 6436, 6682, 6451,
    ],
  },
  {
    name: "Cash — HTB ISA",
    category: "cash",
    rate: 10,
    sortOrder: 4,
    values: [
      15000, 15000, 15000, 15000, 15000, 15000, 15000, 15000, 15000, 15000, 12842, 12842, 12842, 0,
      0, 0, 0,
    ],
  },
  {
    name: "Commodity — Silver",
    category: "commodity",
    rate: 2,
    sortOrder: 5,
    values: [
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      null,
      null,
    ],
  },
  {
    name: "Cash Saver — Hbz",
    category: "cash",
    rate: 5.2,
    sortOrder: 6,
    values: [14000, 14000, 14000, 14000, 14000, 14000, 14000, 14730, 14730, 0, 0, 0, 0, 0, 0, 0, 0],
  },
  {
    name: "Cash Saver — Allica",
    category: "cash",
    rate: 4.6,
    sortOrder: 7,
    values: [10000, 10000, 10000, 10000, 10000, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
  },
  {
    name: "Cash — Monzo",
    category: "cash",
    rate: 0,
    sortOrder: 8,
    values: [500, 500, 265, 2000, 225, 775, 395, 610, 470, 481, 670, 943, 837, 1552, 782, 460, 350],
  },
  {
    name: "Cash — Santander",
    category: "cash",
    rate: 0,
    sortOrder: 9,
    values: [
      4180, 4160, 5357, 2330, 6220, 5910, 5445, 5430, 4800, 6163, 5764, 5306, 6219, 5669, 8070,
      6012, 7867,
    ],
  },
  {
    name: "Cash ISA — Trading 212",
    category: "cash",
    rate: 5.1,
    sortOrder: 10,
    values: [
      1350, 1360, 610, 615, 620, 10645, 8680, 7011, 5525, 17665, 17727, 17774, 17833, 26838, 21996,
      21154, 21213,
    ],
  },
  {
    name: "Debt — Barclays",
    category: "debt",
    rate: 0,
    sortOrder: 11,
    values: [
      -250, -470, -625, -675, -553, -530, -1300, -2269, -1490, -1556, -2122, -1309, -2096, -1896,
      -912, -761, -785,
    ],
  },
  {
    name: "Debt — Amex",
    category: "debt",
    rate: 0,
    sortOrder: 12,
    values: [
      -2420, -2080, -3345, -1550, -2915, -1540, -2465, -3800, -2580, -2344, -1054, -1565, -2600,
      -2964, -4629, -1951, -2700,
    ],
  },
];

async function main() {
  console.log("Clearing existing investment data…");
  await db.investmentSnapshot.deleteMany({});
  await db.investmentAccount.deleteMany({});

  console.log("Seeding investment accounts and snapshots…");

  for (const acc of ACCOUNTS) {
    const account = await db.investmentAccount.create({
      data: { name: acc.name, category: acc.category, rate: acc.rate, sortOrder: acc.sortOrder },
    });

    for (let i = 0; i < DATES.length; i++) {
      const val = acc.values[i];
      if (val === null) continue;
      const date = new Date(DATES[i] + "T00:00:00.000Z");
      await db.investmentSnapshot.create({ data: { accountId: account.id, date, value: val } });
    }

    console.log(`  ✓ ${acc.name}`);
  }

  console.log("Done.");
}

main()
  .catch(console.error)
  .finally(() => db.$disconnect());

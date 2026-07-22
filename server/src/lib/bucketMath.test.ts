import { describe, expect, test } from "bun:test";
import { netBucketSpent, ownerWeight, type BucketTxn } from "./bucketMath.js";

const expense = (owner: string, amount: number, bucket: BucketTxn["bucket"]): BucketTxn => ({
  owner,
  type: "Expense",
  amount,
  bucket,
});
const income = (owner: string, amount: number, bucket: BucketTxn["bucket"]): BucketTxn => ({
  owner,
  type: "Income",
  amount,
  bucket,
});

describe("ownerWeight", () => {
  test("viewer's own transactions count in full", () => {
    expect(ownerWeight("Alex", "Alex")).toBe(1);
  });
  test("Joint counts half", () => {
    expect(ownerWeight("Joint", "Alex")).toBe(0.5);
  });
  test("other people count nothing", () => {
    expect(ownerWeight("Casey", "Alex")).toBe(0);
  });
});

describe("netBucketSpent — Wants gauge (Alex + ½ Joint)", () => {
  test("sums Alex's own Wants expenses in full", () => {
    const txns = [expense("Alex", 100, "Wants"), expense("Alex", 50, "Wants")];
    expect(netBucketSpent(txns, "Wants", "Alex")).toBe(150);
  });

  test("counts Joint Wants at half and excludes Casey entirely", () => {
    const txns = [
      expense("Alex", 100, "Wants"), // full
      expense("Joint", 80, "Wants"), // half → 40
      expense("Casey", 200, "Wants"), // excluded → 0
    ];
    expect(netBucketSpent(txns, "Wants", "Alex")).toBe(140);
  });

  test("a Wants refund (income) nets off the matching spend", () => {
    // £50 jumper bought then returned → net £0 of Wants for the month.
    const txns = [expense("Alex", 50, "Wants"), income("Alex", 50, "Wants")];
    expect(netBucketSpent(txns, "Wants", "Alex")).toBe(0);
  });

  test("a Joint refund nets at half weight", () => {
    // Joint £60 spend (→ £30) partly refunded £20 (→ −£10) = £20 net.
    const txns = [expense("Joint", 60, "Wants"), income("Joint", 20, "Wants")];
    expect(netBucketSpent(txns, "Wants", "Alex")).toBe(20);
  });

  test("only the target bucket is counted — Needs/Savings/Ignore/null are ignored", () => {
    const txns = [
      expense("Alex", 100, "Wants"),
      expense("Alex", 999, "Needs"),
      expense("Alex", 999, "Savings"),
      expense("Alex", 999, "Ignore"),
      expense("Alex", 999, null),
    ];
    expect(netBucketSpent(txns, "Wants", "Alex")).toBe(100);
  });

  test("combined scenario: mixed owners, buckets and a refund", () => {
    const txns = [
      expense("Alex", 120, "Wants"), // +120
      expense("Joint", 100, "Wants"), // +50
      income("Alex", 30, "Wants"), // −30 (refund)
      expense("Casey", 500, "Wants"), // excluded
      expense("Alex", 400, "Needs"), // wrong bucket
    ];
    expect(netBucketSpent(txns, "Wants", "Alex")).toBe(140);
  });
});

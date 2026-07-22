import { describe, it, expect } from "vitest";
import { aggregateMonthlySavings, ownerWeight, type SavingsTxn } from "./savings.js";

const exp = (owner: string, amount: number, bucket: SavingsTxn["bucket"], date = "2026-03-10") =>
  ({ owner, amount, bucket, date, type: "Expense" }) as SavingsTxn;
const inc = (owner: string, amount: number, bucket: SavingsTxn["bucket"], date = "2026-03-10") =>
  ({ owner, amount, bucket, date, type: "Income" }) as SavingsTxn;

const MONTH = "2026-03";

describe("ownerWeight", () => {
  it("counts the viewer in full, Joint at half, others at nothing", () => {
    expect(ownerWeight("Alex", "Alex")).toBe(1);
    expect(ownerWeight("Joint", "Alex")).toBe(0.5);
    expect(ownerWeight("Casey", "Alex")).toBe(0);
  });
});

describe("aggregateMonthlySavings", () => {
  it("salary (uncategorised income) counts as real income", () => {
    const agg = aggregateMonthlySavings([inc("Alex", 5000, null)], "Alex");
    expect(agg[MONTH].saved).toBe(5000);
    expect(agg[MONTH].variable).toBe(0);
  });

  it("Needs and Wants spend reduce saved; Wants also feeds the variable footnote", () => {
    const agg = aggregateMonthlySavings(
      [inc("Alex", 5000, null), exp("Alex", 1500, "Needs"), exp("Alex", 800, "Wants")],
      "Alex",
    );
    expect(agg[MONTH].saved).toBe(5000 - 1500 - 800);
    expect(agg[MONTH].variable).toBe(800);
  });

  it("uncategorised (null) expense still counts as spend but not as Wants", () => {
    const agg = aggregateMonthlySavings([inc("Alex", 1000, null), exp("Alex", 200, null)], "Alex");
    expect(agg[MONTH].saved).toBe(800);
    expect(agg[MONTH].variable).toBe(0);
  });

  it("excludes Savings and Ignore in BOTH directions", () => {
    const agg = aggregateMonthlySavings(
      [
        inc("Alex", 1000, null),
        exp("Alex", 300, "Savings"), // money set aside — not a spend
        inc("Alex", 300, "Savings"), // pulled back out — not income
        exp("Alex", 400, "Ignore"), // transfer — ignored
        inc("Alex", 400, "Ignore"), // transfer in — ignored
      ],
      "Alex",
    );
    expect(agg[MONTH].saved).toBe(1000);
    expect(agg[MONTH].variable).toBe(0);
  });

  it("a Wants refund nets off the matching spend in both saved and variable", () => {
    // £50 jumper bought then returned → nets to zero.
    const agg = aggregateMonthlySavings(
      [exp("Alex", 50, "Wants"), inc("Alex", 50, "Wants")],
      "Alex",
    );
    expect(agg[MONTH].saved).toBe(0);
    expect(agg[MONTH].variable).toBe(0);
  });

  it("applies owner weighting: viewer full, Joint half, Casey excluded", () => {
    const agg = aggregateMonthlySavings(
      [
        exp("Alex", 100, "Wants"), // −100
        exp("Joint", 80, "Wants"), // −40
        exp("Casey", 500, "Wants"), // excluded
      ],
      "Alex",
    );
    expect(agg[MONTH].saved).toBe(-140);
    expect(agg[MONTH].variable).toBe(140);
  });

  it("a Joint Wants refund nets at half weight", () => {
    // Joint £60 spend (→ 30) minus £20 refund (→ 10) = 20 net Wants.
    const agg = aggregateMonthlySavings(
      [exp("Joint", 60, "Wants"), inc("Joint", 20, "Wants")],
      "Alex",
    );
    expect(agg[MONTH].variable).toBe(20);
    expect(agg[MONTH].saved).toBe(-20);
  });

  it("groups by calendar month", () => {
    const agg = aggregateMonthlySavings(
      [inc("Alex", 1000, null, "2026-01-05"), exp("Alex", 200, "Wants", "2026-02-18")],
      "Alex",
    );
    expect(agg["2026-01"].saved).toBe(1000);
    expect(agg["2026-02"].saved).toBe(-200);
    expect(agg["2026-02"].variable).toBe(200);
  });

  it("parses string amounts (as they arrive from the API)", () => {
    const agg = aggregateMonthlySavings(
      [{ owner: "Alex", amount: "123.45", bucket: "Needs", date: "2026-03-01", type: "Expense" }],
      "Alex",
    );
    expect(agg[MONTH].saved).toBeCloseTo(-123.45, 2);
  });
});

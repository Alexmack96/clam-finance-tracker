import { describe, it, expect } from "vitest";
import type { Bucket, Rule, RuleCondition } from "@clam/core";
import { categoriesWithoutDefaultBucket } from "./bucketCoverage.js";

let nextId = 0;

function bucketRule(
  bucket: Bucket,
  conditions: Omit<RuleCondition, "id" | "position">[],
  bank: Rule["bank"] = null,
): Rule {
  const id = `r${nextId++}`;
  return {
    id,
    kind: "Bucket",
    position: nextId,
    joinOperator: "AND",
    bank,
    categoryId: null,
    bucket,
    conditions: conditions.map((c, i) => ({ ...c, id: `${id}c${i}`, position: i })),
    createdAt: "2026-01-01T00:00:00.000Z",
  };
}

const category = (name: string, negate = false): Omit<RuleCondition, "id" | "position"> => ({
  field: "Category",
  operator: "Exact",
  value: name,
  negate,
});

const names = (list: { name: string }[]) => list.map((c) => c.name);

describe("categoriesWithoutDefaultBucket", () => {
  it("lists a category no rule mentions", () => {
    const rules = [bucketRule("Needs", [category("Groceries")])];
    expect(
      names(categoriesWithoutDefaultBucket([{ name: "Groceries" }, { name: "Pets" }], rules)),
    ).toEqual(["Pets"]);
  });

  it("does not count an exception as the default", () => {
    const uber = bucketRule("Wants", [
      category("Transport"),
      { field: "Description", operator: "Contains", value: "uber", negate: false },
    ]);
    expect(names(categoriesWithoutDefaultBucket([{ name: "Transport" }], [uber]))).toEqual([
      "Transport",
    ]);
  });

  it("counts a rule scoped to one transaction type", () => {
    const refunds = bucketRule("Wants", [
      category("Shopping"),
      { field: "Type", operator: "Exact", value: "Income", negate: false },
    ]);
    expect(categoriesWithoutDefaultBucket([{ name: "Shopping" }], [refunds])).toEqual([]);
  });

  it("does not count a rule scoped to one bank", () => {
    const monzoOnly = bucketRule("Needs", [category("Bills")], "monzo");
    expect(names(categoriesWithoutDefaultBucket([{ name: "Bills" }], [monzoOnly]))).toEqual([
      "Bills",
    ]);
  });

  it("leaves Uncategorised out", () => {
    expect(categoriesWithoutDefaultBucket([{ name: "Uncategorised" }], [])).toEqual([]);
  });
});

import { describe, expect, test } from "bun:test";
import { bankOf, matchesRule, resolveAssignment, resolveRule } from "./ruleEngine.js";
import type { Rule, RuleCondition, RuleField, RuleOperator } from "../schemas/rules.js";
import type { MatchableTransaction } from "./ruleEngine.js";

let seq = 0;
function cond(
  value: string,
  operator: RuleOperator = "Contains",
  negate = false,
  field: RuleField = "Description",
): RuleCondition {
  return { id: `c${seq++}`, field, operator, value, negate, position: 0 };
}

function rule(overrides: Partial<Rule> & { conditions: RuleCondition[] }): Rule {
  return {
    id: `r${seq++}`,
    kind: "Category",
    position: 0,
    joinOperator: "AND",
    bank: null,
    categoryId: "cat-a",
    bucket: null,
    createdAt: "2026-01-01T00:00:00.000Z",
    ...overrides,
  };
}

function tx(overrides: Partial<MatchableTransaction> = {}): MatchableTransaction {
  return {
    description: "Richmond Park Golf Course",
    type: "Expense",
    categoryName: "Uncategorised",
    bank: "monzo",
    ...overrides,
  };
}

describe("operators", () => {
  const cases: [RuleOperator, string, boolean][] = [
    ["Contains", "park golf", true],
    ["Contains", "nope", false],
    ["StartsWith", "richmond", true],
    ["StartsWith", "park", false],
    ["EndsWith", "course", true],
    ["EndsWith", "richmond", false],
    ["Exact", "richmond park golf course", true],
    ["Exact", "richmond park golf", false],
  ];

  for (const [operator, value, expected] of cases) {
    test(`${operator} "${value}" → ${expected}`, () => {
      expect(matchesRule(tx(), rule({ conditions: [cond(value, operator)] }))).toBe(expected);
    });
  }

  test("matching is case-insensitive and trims the needle", () => {
    expect(matchesRule(tx(), rule({ conditions: [cond("  GOLF  ")] }))).toBe(true);
  });
});

describe("join operator and exclusions", () => {
  test("AND requires every positive", () => {
    expect(matchesRule(tx(), rule({ conditions: [cond("Richmond"), cond("Golf")] }))).toBe(true);
    expect(matchesRule(tx(), rule({ conditions: [cond("Richmond"), cond("Tesco")] }))).toBe(false);
  });

  test("OR requires only one positive", () => {
    const r = rule({ joinOperator: "OR", conditions: [cond("Tesco"), cond("Golf")] });
    expect(matchesRule(tx(), r)).toBe(true);
  });

  test("a negated condition excludes even when a positive matches", () => {
    const r = rule({ conditions: [cond("Golf"), cond("Richmond", "Contains", true)] });
    expect(matchesRule(tx(), r)).toBe(false);
  });

  // The divergence from ag-grid: the join operator governs the positives only,
  // so exclusions still apply under OR. This is what makes
  // "(A OR B) AND NOT C" expressible without a nesting UI.
  test("exclusions are ANDed even when the join operator is OR", () => {
    const r = rule({
      joinOperator: "OR",
      conditions: [cond("Tesco"), cond("Golf"), cond("Richmond", "Contains", true)],
    });
    expect(matchesRule(tx(), r)).toBe(false);

    const kept = rule({
      joinOperator: "OR",
      conditions: [cond("Tesco"), cond("Golf"), cond("Wimbledon", "Contains", true)],
    });
    expect(matchesRule(tx(), kept)).toBe(true);
  });

  test("a rule with only exclusions never matches", () => {
    const r = rule({ conditions: [cond("Tesco", "Contains", true)] });
    expect(matchesRule(tx(), r)).toBe(false);
  });

  // The real case this redesign was built for: contains "Golf" is the right
  // operator, but it swallows a bank transfer from a mate.
  test("Golf minus FASTER PAYMENTS", () => {
    const r = rule({
      conditions: [cond("Golf"), cond("FASTER PAYMENTS", "Contains", true)],
    });
    expect(matchesRule(tx(), r)).toBe(true);
    expect(
      matchesRule(tx({ description: "FASTER PAYMENTS RECEIPT REF.Golf ting FROM Izak" }), r),
    ).toBe(false);
  });
});

describe("bank scope", () => {
  test("a null bank matches any bank", () => {
    expect(matchesRule(tx({ bank: "amex" }), rule({ conditions: [cond("Golf")] }))).toBe(true);
  });

  test("a bank-scoped rule only matches that bank", () => {
    const r = rule({ bank: "monzo", conditions: [cond("Golf")] });
    expect(matchesRule(tx({ bank: "monzo" }), r)).toBe(true);
    expect(matchesRule(tx({ bank: "hsbc" }), r)).toBe(false);
  });
});

describe("precedence", () => {
  // Position is the whole of precedence — no specificity heuristic. A broad
  // rule above a precise one wins, and the fix is to reorder, not to out-clever
  // a scoring function.
  test("first match by position wins, regardless of how specific", () => {
    const broad = rule({ position: 0, categoryId: "broad", conditions: [cond("Golf")] });
    const precise = rule({
      position: 1,
      categoryId: "precise",
      conditions: [cond("Richmond Park Golf Course", "Exact")],
    });
    expect(resolveRule(tx(), [broad, precise])?.categoryId).toBe("broad");
    expect(
      resolveRule(tx(), [
        { ...precise, position: 0 },
        { ...broad, position: 1 },
      ])?.categoryId,
    ).toBe("precise");
  });

  test("input order does not matter, only position", () => {
    const first = rule({ position: 0, categoryId: "first", conditions: [cond("Golf")] });
    const second = rule({ position: 1, categoryId: "second", conditions: [cond("Golf")] });
    expect(resolveRule(tx(), [second, first])?.categoryId).toBe("first");
  });
});

describe("two-pass pipeline", () => {
  const names = new Map([
    ["cat-transport", "Transport"],
    ["cat-uncat", "Uncategorised"],
  ]);

  const toTransport = rule({
    kind: "Category",
    position: 0,
    categoryId: "cat-transport",
    conditions: [cond("uber")],
  });

  test("a Bucket rule sees the Category a Category rule just assigned", () => {
    const uberIsWants = rule({
      kind: "Bucket",
      position: 0,
      categoryId: null,
      bucket: "Wants",
      conditions: [cond("Transport", "Exact", false, "Category"), cond("uber")],
    });
    const transportIsNeeds = rule({
      kind: "Bucket",
      position: 1,
      categoryId: null,
      bucket: "Needs",
      conditions: [cond("Transport", "Exact", false, "Category")],
    });

    const rules = [toTransport, uberIsWants, transportIsNeeds];

    const uber = resolveAssignment(
      tx({ description: "UBER TRIP HELP.UBER.COM", categoryName: "Uncategorised" }),
      rules,
      names,
    );
    expect(uber.categoryId).toBe("cat-transport");
    expect(uber.bucket).toBe("Wants");

    // Same category, no "uber" in the description — falls through to Needs.
    const train = resolveAssignment(
      tx({ description: "TFL TRAVEL CHARGE", categoryName: "Transport" }),
      rules,
      names,
    );
    expect(train.categoryId).toBeNull();
    expect(train.bucket).toBe("Needs");
  });

  test("a Category condition is vacuously true when negated and the category is unknown", () => {
    const r = rule({
      kind: "Bucket",
      categoryId: null,
      bucket: "Wants",
      conditions: [cond("Golf"), cond("Transport", "Exact", true, "Category")],
    });
    expect(matchesRule(tx({ categoryName: null }), r)).toBe(true);
    expect(matchesRule(tx({ categoryName: "Transport" }), r)).toBe(false);
  });

  test("Type is matchable, so a rule can be scoped to expenses only", () => {
    const r = rule({ conditions: [cond("Golf"), cond("Expense", "Exact", false, "Type")] });
    expect(matchesRule(tx({ type: "Expense" }), r)).toBe(true);
    expect(matchesRule(tx({ type: "Income" }), r)).toBe(false);
  });
});

describe("bankOf", () => {
  test.each([
    ["monzo:tx_123", "monzo"],
    ["santander-plaid:abc", "santander-plaid"],
    [null, null],
    ["", null],
    ["no-colon", null],
  ])("%s → %s", (input, expected) => {
    expect(bankOf(input)).toBe(expected);
  });
});

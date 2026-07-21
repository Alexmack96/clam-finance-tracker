import { describe, it, expect } from "vitest";
import { bankSource, BANK_SOURCES, SOURCE_STYLES, type BankSource } from "./bankSource.js";

// externalId is namespaced per bank ("monzo:tx_...", "amex:ref_..."). bankSource()
// derives the display/filter Source from that prefix; SourceFilter.doesFilterPass and
// the mobile source dropdown both match on its output.
describe("bankSource", () => {
  const cases: [string, BankSource][] = [
    ["monzo:tx_123", "Monzo"],
    ["flex:tx_123", "Flex"],
    ["amex:ref_123", "Amex"],
    ["barclays:abc", "Barclays"],
    ["santander:abc", "Santander"],
    ["hsbc:abc", "HSBC"],
    ["sofi:abc", "SoFi"],
    ["chase:abc", "Chase"],
  ];

  it.each(cases)("maps %s → %s", (externalId, expected) => {
    expect(bankSource(externalId)).toBe(expected);
  });

  it("returns Manual for a null externalId", () => {
    expect(bankSource(null)).toBe("Manual");
  });

  it("returns Manual for an unknown / un-namespaced externalId", () => {
    expect(bankSource("")).toBe("Manual");
    expect(bankSource("weird_id_no_prefix")).toBe("Manual");
    expect(bankSource("Monzo:tx_123")).toBe("Manual"); // prefix match is case-sensitive
  });
});

// Guards against drift: adding a bank to bankSource() without also making it
// selectable (BANK_SOURCES) or styleable (SOURCE_STYLES) is a silent bug — a
// transaction whose source can never be filtered. This is exactly the gap that
// left "Flex" out of the filter list before.
describe("source list stays in sync", () => {
  it("every source with a style is selectable in the filter list", () => {
    expect([...BANK_SOURCES].sort()).toEqual(Object.keys(SOURCE_STYLES).sort());
  });

  it("every selectable source round-trips through bankSource via its prefix", () => {
    for (const source of BANK_SOURCES) {
      if (source === "Manual") continue; // Manual has no prefix; it's the fallback
      expect(bankSource(`${source.toLowerCase()}:x`)).toBe(source);
    }
  });
});

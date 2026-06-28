import { describe, it, expect } from "vitest";
import { findLatestSalary } from "./salary.js";

// The Santander statement description doesn't byte-match the old hardcoded string —
// it carries HUDSON BAY but with extra reference/spacing noise. We identify salary by
// "hudson bay" (case-insensitive) + amount > £5k, which is a safe signature.
const salaryMay = {
  description: "FASTER PAYMENTS RECEIPT REF.HUDSON BAY    FROM THROGMORTON UK LTD 0512",
  amount: "5515.17",
  date: "2026-05-30",
};
const salaryApr = {
  description: "Faster Payments Receipt Ref.Hudson Bay From Throgmorton Uk Ltd",
  amount: "5515.17",
  date: "2026-04-30",
};

describe("findLatestSalary", () => {
  it("matches a Hudson Bay salary even when the description isn't an exact string match", () => {
    expect(findLatestSalary([salaryMay])).toBe(5515.17);
  });

  it("ignores small Hudson Bay transactions (e.g. a shop refund under £5k)", () => {
    const refund = { description: "HUDSON BAY CO RETAIL", amount: "42.00", date: "2026-05-12" };
    expect(findLatestSalary([refund])).toBeNull();
  });

  it("ignores large non-salary income that isn't Hudson Bay", () => {
    const other = {
      description: "FASTER PAYMENT FROM SOMEONE ELSE",
      amount: "9000.00",
      date: "2026-05-01",
    };
    expect(findLatestSalary([other])).toBeNull();
  });

  it("returns the most recent salary when several exist", () => {
    const newer = { ...salaryMay, amount: "5600.00" };
    expect(findLatestSalary([salaryApr, newer])).toBe(5600);
  });

  it("returns null when there are no transactions", () => {
    expect(findLatestSalary([])).toBeNull();
  });
});

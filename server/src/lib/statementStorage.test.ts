import { test, expect, describe, afterAll } from "bun:test";
import { mkdtempSync, rmSync, existsSync } from "fs";
import { tmpdir } from "os";
import { join, resolve } from "path";
import { createStatementStore } from "./statementStorage.js";

const base = mkdtempSync(join(tmpdir(), "statements-"));
const store = createStatementStore(base);
afterAll(() => rmSync(base, { recursive: true, force: true }));

const HASH = "a".repeat(64);

describe("keyFor", () => {
  test("builds a self-describing key under bank/owner", () => {
    expect(
      store.keyFor({ bank: "amex", owner: "Alex", statementDate: "24/07/26", contentHash: HASH }),
    ).toBe("amex/Alex/24-07-26-aaaaaaaaaaaa.pdf");
  });

  test("an unparsed statement still gets a key", () => {
    expect(
      store.keyFor({ bank: "amex", owner: "Casey", statementDate: null, contentHash: HASH }),
    ).toBe("amex/Casey/undated-aaaaaaaaaaaa.pdf");
  });

  test("two statements sharing a date don't collide", () => {
    const a = store.keyFor({
      bank: "amex",
      owner: "Alex",
      statementDate: "24/07/26",
      contentHash: HASH,
    });
    const b = store.keyFor({
      bank: "amex",
      owner: "Alex",
      statementDate: "24/07/26",
      contentHash: "b".repeat(64),
    });
    expect(a).not.toBe(b);
  });

  test("path separators in an owner can't create directories", () => {
    const key = store.keyFor({
      bank: "amex",
      owner: "../../etc",
      statementDate: null,
      contentHash: HASH,
    });
    expect(key).toBe("amex/etc/undated-aaaaaaaaaaaa.pdf");
    expect(store.pathFor(key).startsWith(resolve(base))).toBe(true);
  });
});

describe("pathFor rejects keys that escape the statements directory", () => {
  // Second line of defence: keys come out of the database, so a tampered or
  // legacy row must not be able to read or unlink arbitrary files.
  test.each([
    ["../../../etc/passwd"],
    ["amex/../../outside.pdf"],
    ["amex/Alex/../../../../outside.pdf"],
  ])("%s", (key) => {
    expect(() => store.pathFor(key)).toThrow(/escapes/);
  });

  test("absolute paths are rejected", () => {
    expect(() => store.pathFor(resolve(base, "..", "outside.pdf"))).toThrow(/must be relative/);
  });

  test("a legitimate nested key resolves inside the base", () => {
    const full = store.pathFor("amex/Alex/24-07-26-abc.pdf");
    expect(full.startsWith(resolve(base))).toBe(true);
  });
});

describe("save / read / remove", () => {
  test("round-trips the exact bytes", async () => {
    const key = store.keyFor({
      bank: "amex",
      owner: "Alex",
      statementDate: "24/07/26",
      contentHash: HASH,
    });
    const data = Buffer.from("%PDF-1.4 not really a pdf");
    await store.save(key, data);
    expect(await store.read(key)).toEqual(data);
  });

  test("creates the bank/owner directories on the way", async () => {
    const key = "hsbc/Casey/09-05-26-deadbeef.pdf";
    await store.save(key, Buffer.from("x"));
    expect(existsSync(store.pathFor(key))).toBe(true);
  });

  test("remove deletes the file", async () => {
    const key = "amex/Alex/gone-1234.pdf";
    await store.save(key, Buffer.from("x"));
    await store.remove(key);
    expect(existsSync(store.pathFor(key))).toBe(false);
  });

  test("removing a file that's already gone is not an error", async () => {
    // Deleting a statement whose PDF was lost must still clear the DB rows.
    expect(await store.remove("amex/Alex/never-existed.pdf")).toBeUndefined();
  });
});

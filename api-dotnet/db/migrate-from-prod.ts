#!/usr/bin/env bun
/**
 * Emits the production SQLite database's reference and configuration data as a
 * T-SQL MERGE script, on stdout.
 *
 * What it copies, and what it does not:
 *
 *   Copied      categories, users, rules and their conditions, investment
 *               accounts and snapshots, tabs, recurring verdicts, notes.
 *   Not copied  transactions, every bank's staging table, statement files,
 *               Monzo reconciliation runs. Those are history and are migrated
 *               separately, once.
 *   Never       Better Auth's account/session/verification tables (retired with
 *               it) and the Monzo OAuth credential (a live token; reconnect
 *               instead of copying a secret between databases).
 *
 * **Ids are preserved.** Rules point at categories by id, snapshots point at
 * accounts by id, and the transactions still to come point at both. Minting new
 * ids here would silently sever all of that.
 *
 * Generates SQL rather than connecting to SQL Server itself. A driver here would
 * be a second way to reach the database, with its own connection-string parsing
 * and its own failure modes, for a script that runs a handful of times — sqlcmd
 * already works and is what applies db/schema.sql.
 *
 * Usage, from the repo root:
 *
 *     bun run db-snapshot                                  # refresh prod.db
 *     bun api-dotnet/db/migrate-from-prod.ts > _migrate.sql
 *     sqlcmd -S tcp:<server>,1433 -d ClamFinanceDev -U <user> -P <pw> -i ./_migrate.sql -I -b
 *
 * Pass sqlcmd a *relative* path. It rejects `C:/...` with "Access is denied".
 *
 * Idempotent: every table is a MERGE on the primary key, so re-running updates
 * rows rather than failing, and it can be run again after prod moves on.
 */
import { Database } from "bun:sqlite";

type ColumnKind = "text" | "date" | "bool" | "number";

interface Column {
  name: string;
  kind: ColumnKind;
  /** NVARCHAR width in db/schema.sql. Checked before emitting, not after failing. */
  max?: number;
}

interface TableCopy {
  /** Prisma's table name in SQLite. Casing varies: `Rule` but `investment_account`. */
  source: string;
  target: string;
  columns: Column[];
  /** Column to match on. Defaults to the primary key. */
  matchOn?: string;
  /**
   * Set when a non-matching source row must NOT be inserted, only used to update
   * rows that already exist. See [Users].
   */
  updateOnly?: { set: string[] };
}

const t = (name: string, max?: number): Column => ({ name, kind: "text", max });
const d = (name: string): Column => ({ name, kind: "date" });
const b = (name: string): Column => ({ name, kind: "bool" });
const n = (name: string): Column => ({ name, kind: "number" });

/**
 * Order is foreign-key order and must stay that way: categories before the rules
 * that reference them, accounts before their snapshots.
 */
const TABLES: TableCopy[] = [
  {
    source: "Category",
    target: "Categories",
    columns: [t("id", 50), t("name", 100), t("color", 20)],
  },
  {
    // Matched on email, and it only ever *updates*. Identity lives in WorkOS
    // now, and the sole thing prod's user table still knows that WorkOS does not
    // is which of Alex and Casey a login is — the `owner` every page defaults to.
    //
    // Inserting would be worse than useless. Prod's ids are not referenced by
    // anything migrated here (transactions carry no userId, and the Monzo
    // credential is not copied), so a fresh row buys nothing — and it takes the
    // email, so when that person first signs in, GET /api/me answers 404 and
    // POST /api/users/me is then refused as a duplicate address. That is a dead
    // end reachable only by a person who has never signed in, which is the worst
    // time to hit it.
    //
    // So the order is: sign in first, which self-provisions a WorkOS-linked row,
    // then re-run this to stamp `owner` onto it.
    source: "user",
    target: "Users",
    matchOn: "email",
    updateOnly: { set: ["owner"] },
    columns: [t("email", 320), t("owner", 10)],
  },
  {
    source: "Rule",
    target: "Rules",
    columns: [
      t("id", 50),
      t("kind", 10),
      n("position"),
      t("joinOperator", 3),
      t("bank", 30),
      t("categoryId", 50),
      t("bucket", 10),
      d("createdAt"),
    ],
  },
  {
    source: "RuleCondition",
    target: "RuleConditions",
    columns: [
      t("id", 50),
      t("ruleId", 50),
      t("field", 20),
      t("operator", 20),
      t("value", 200),
      b("negate"),
      n("position"),
    ],
  },
  {
    source: "investment_account",
    target: "InvestmentAccounts",
    columns: [
      t("id", 50),
      t("name", 200),
      t("category", 20),
      t("owner", 10),
      n("rate"),
      n("sortOrder"),
      d("createdAt"),
      d("updatedAt"),
    ],
  },
  {
    source: "investment_snapshot",
    target: "InvestmentSnapshots",
    columns: [t("id", 50), t("accountId", 50), d("date"), n("value"), d("createdAt"), d("updatedAt")],
  },
  {
    source: "tab",
    target: "Tabs",
    columns: [
      t("id", 50),
      t("person", 200),
      t("description", 500),
      n("amount"),
      t("direction", 10),
      t("status", 10),
      d("dueDate"),
      d("settledAt"),
      t("note"),
      d("createdAt"),
      d("updatedAt"),
    ],
  },
  {
    source: "recurring_verdict",
    target: "RecurringVerdicts",
    columns: [
      t("id", 50),
      t("owner", 10),
      t("description", 400),
      t("status", 10),
      t("note", 500),
      d("createdAt"),
      d("updatedAt"),
    ],
  },
  {
    source: "note",
    target: "Notes",
    columns: [t("id", 50), t("title", 300), t("body"), b("pinned"), d("createdAt"), d("updatedAt")],
  },
];

/** T-SQL string escaping is one rule: double any single quote. */
const quote = (value: string) => `'${value.replace(/'/g, "''")}'`;

/**
 * Prisma stores DateTime as an ISO string carrying an offset (`+00:00`).
 * DATETIME2 has no offset, and SQL Server will not take a literal with one, so
 * the instant is converted to UTC and the offset dropped rather than trimmed
 * blindly — a non-zero offset would otherwise shift the time silently.
 */
function toSqlDateTime(value: unknown): string {
  const parsed = new Date(String(value));
  if (Number.isNaN(parsed.getTime())) throw new Error(`Unparseable date: ${String(value)}`);
  return quote(parsed.toISOString().replace("Z", "").replace("T", " "));
}

function literal(column: Column, value: unknown, table: string): string {
  if (value === null || value === undefined) return "NULL";

  switch (column.kind) {
    case "date":
      return toSqlDateTime(value);
    case "bool":
      return value ? "1" : "0";
    case "number":
      return String(value);
    case "text": {
      const text = String(value);
      if (column.max && text.length > column.max) {
        throw new Error(
          `${table}.${column.name} is NVARCHAR(${column.max}) but a value is ${text.length} ` +
            `characters: ${text.slice(0, 60)}…`,
        );
      }
      return quote(text);
    }
  }
}

const db = new Database("prod.db", { readonly: true });
const out: string[] = [];
const summary: string[] = [];

out.push("-- Generated by api-dotnet/db/migrate-from-prod.ts");
out.push("-- Source: prod.db (Railway volume snapshot).");
out.push("-- Reference and configuration data only: no transactions, no staging, no secrets.");
out.push("-- Idempotent — every statement is a MERGE on the primary key.");
out.push("");

for (const table of TABLES) {
  const rows = db.query<Record<string, unknown>, []>(`SELECT * FROM "${table.source}"`).all();
  summary.push(`${String(rows.length).padStart(5)}  ${table.source} -> ${table.target}`);

  if (rows.length === 0) {
    out.push(`-- ${table.target}: nothing in prod.`);
    out.push("");
    continue;
  }

  const names = table.columns.map((c) => `[${c.name}]`).join(", ");
  const values = rows
    .map((row) => {
      const cells = table.columns.map((c) => literal(c, row[c.name], table.target));
      return `    (${cells.join(", ")})`;
    })
    .join(",\n");

  const key = table.matchOn ?? "id";

  // Everything but the key is updated on a match, so a re-run reflects prod
  // rather than skipping rows that already exist.
  const updated = table.updateOnly?.set ?? table.columns.map((c) => c.name).filter((c) => c !== key);
  const updates = updated.map((c) => `[${c}] = source.[${c}]`).join(",\n        ");

  out.push(`-- ${table.target}: ${rows.length} row(s) from ${table.source}.`);
  out.push(`MERGE [${table.target}] AS target`);
  out.push("USING (VALUES");
  out.push(values);
  out.push(`) AS source (${names})`);
  out.push(`    ON target.[${key}] = source.[${key}]`);
  out.push("WHEN MATCHED THEN");
  out.push(`    UPDATE SET ${updates}`);

  if (!table.updateOnly) {
    out.push("WHEN NOT MATCHED THEN");
    out.push(
      `    INSERT (${names}) VALUES (${table.columns.map((c) => `source.[${c.name}]`).join(", ")})`,
    );
  }

  out.push(";");
  out.push("");
}

db.close();

process.stdout.write(out.join("\n"));
console.error("Rows read from prod.db:");
console.error(summary.join("\n"));
console.error(
  "\n[Users] only updates `owner` on rows that already exist, matched by email.\n" +
    "Identity is WorkOS's now, so each person signs in first — which creates their row —\n" +
    "and a re-run of this script then stamps their owner onto it. Anyone who has not\n" +
    "signed in yet is simply skipped; run it again once they have.",
);

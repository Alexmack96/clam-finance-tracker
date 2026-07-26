import { mkdir, readFile, unlink, writeFile } from "fs/promises";
import { dirname, isAbsolute, join, resolve, sep } from "path";
import { env } from "../config/env.js";

// Statement PDFs live on the same Railway volume as the SQLite database, under a
// `statements/` directory beside the .db file. Keeping them next to the DB means
// the volume is the single thing to back up, and no extra vendor or credential is
// involved. The trade-off is a shared blast radius: losing the volume loses both
// the database and the source documents.

/// Derive the statements directory from DATABASE_URL so it follows the database
/// automatically — `file:/data/prod.db` → `/data/statements`, `file:./dev.db` →
/// `./statements`. STATEMENTS_DIR overrides it (used by tests).
export function defaultStatementsDir(): string {
  if (env.STATEMENTS_DIR) return resolve(env.STATEMENTS_DIR);
  const filePath = env.DATABASE_URL.replace(/^file:/, "");
  return join(dirname(resolve(filePath)), "statements");
}

/// Path segments are built from user-influenced values (owner, original filename,
/// statement date), so strip anything that isn't safe in a path component. This is
/// the first of two defences; see `pathFor` for the second.
function slug(value: string): string {
  return (
    value
      .replace(/[^A-Za-z0-9._-]+/g, "-")
      .replace(/^[-.]+|[-.]+$/g, "")
      .slice(0, 60) || "unknown"
  );
}

export interface StatementStore {
  /// Absolute path for a key, guaranteed to sit inside the base directory.
  pathFor(key: string): string;
  keyFor(parts: {
    bank: string;
    owner: string;
    statementDate?: string | null;
    contentHash: string;
  }): string;
  save(key: string, data: Buffer): Promise<void>;
  read(key: string): Promise<Buffer>;
  remove(key: string): Promise<void>;
}

export function createStatementStore(baseDir: string): StatementStore {
  const base = resolve(baseDir);

  function pathFor(key: string): string {
    // Never trust a stored key: a tampered or legacy DB row must not be able to
    // read or delete files outside the statements directory.
    if (isAbsolute(key)) throw new Error(`Statement key must be relative: ${key}`);
    const full = resolve(base, key);
    if (full !== base && !full.startsWith(base + sep))
      throw new Error(`Statement key escapes the statements directory: ${key}`);
    return full;
  }

  return {
    pathFor,

    keyFor({ bank, owner, statementDate, contentHash }) {
      // Hash suffix keeps keys unique even when two statements share a date, and
      // makes the file self-identifying if you're poking around on the volume.
      const date = statementDate ? slug(statementDate) : "undated";
      return `${slug(bank)}/${slug(owner)}/${date}-${slug(contentHash).slice(0, 12)}.pdf`;
    },

    async save(key, data) {
      const full = pathFor(key);
      await mkdir(dirname(full), { recursive: true });
      await writeFile(full, data);
    },

    async read(key) {
      return readFile(pathFor(key));
    },

    async remove(key) {
      try {
        await unlink(pathFor(key));
      } catch (err) {
        // Already gone is success — deleting a statement whose file was lost
        // should still clear the database rows.
        if ((err as NodeJS.ErrnoException).code !== "ENOENT") throw err;
      }
    },
  };
}

export const statementStore = createStatementStore(defaultStatementsDir());

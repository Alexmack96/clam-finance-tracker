/**
 * The width of an id column, which is the same in every table: a cuid in
 * `NVARCHAR(30)`. Mirrors `Domain/Ids.cs` in the .NET API — the two halves of one
 * contract, so a value the client accepts is a value the server can store.
 *
 * Only writes need the rule. An id in a WHERE clause is compared, not stored, so
 * an over-long one is a miss and answers 404 on its own; the same value in an
 * insert is a truncation error, which is a 500 for the caller's mistake.
 */
export const ID_MAX_LENGTH = 30;

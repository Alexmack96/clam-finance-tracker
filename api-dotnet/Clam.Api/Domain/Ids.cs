namespace Clam.Api.Domain;

/// The width of an id column in db/schema.sql: `NVARCHAR(50)`. Most ids are
/// 25-character cuids, but not all — two categories carried over from Express
/// have 36-character UUIDs. This was 30 once, which matched no column and
/// rejected those categories as "not an id" on every write.
///
/// Here rather than in a slice for the same reason as <c>Enums.cs</c> — it is the
/// database's contract, not one feature's opinion, and a slice cannot pick a
/// different number without lying about what the column accepts. Three features
/// now write a caller-supplied id straight into such a column, which is what
/// moved it out of <c>UpdateTransactionValidator</c>.
///
/// Only writes need the rule. A caller-supplied id in a WHERE clause is compared,
/// not stored, so an over-long one is a miss and answers 404 on its own; the same
/// value in an INSERT or an UPDATE is "String or binary data would be truncated",
/// which is a 500 for what is squarely the caller's mistake.
public static class Ids
{
    public const int MaxLength = 50;
}

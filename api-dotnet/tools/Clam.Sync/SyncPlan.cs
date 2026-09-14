using Clam.Migrate;

namespace Clam.Sync;

/// A column that points at another table's id, and which table that is.
///
/// Only tables that can be <see cref="SyncKey.RemapsIds"/> matter here: a
/// reference into one of those has to be rewritten before it is inserted, or it
/// points at an id the target does not have.
public sealed record ForeignKey(string Column, string References);

/// How one table decides a row is already there.
///
/// <param name="Target">The SQL Server table, matching a MigrationPlan entry.</param>
/// <param name="Key">The columns that identify a row across both databases. For
/// most tables the primary key; for the five staging tables whose primary key is
/// an IDENTITY the target assigns, the bank's own <c>transactionId</c>, which is
/// the thing with meaning and the thing carried in the CSV.</param>
/// <param name="NaturalKey">A unique constraint other than the key, if the table
/// has one. Checked as well as the key, because a row created on the target side
/// independently — by the system category seeder, or by hand — has a different
/// id but would collide on this.</param>
/// <param name="RemapsIds">Build source-id → target-id for rows matched on the
/// natural key, and rewrite every <see cref="ForeignKey"/> pointing here. Without
/// it, a skipped Category leaves every Transaction referencing it orphaned, and
/// the insert fails on the foreign key.</param>
/// <param name="ForeignKeys">References that need that rewrite.</param>
public sealed record SyncKey(
    string Target,
    string[] Key,
    string[]? NaturalKey = null,
    bool RemapsIds = false,
    ForeignKey[]? ForeignKeys = null);

/// The key metadata MigrationPlan does not carry, because a one-shot move into an
/// empty database has no use for it.
///
/// **This is a top-up, not a two-way sync.** A row is inserted if the target has
/// nothing matching its key or its natural key, and otherwise left completely
/// alone. An edit made in production after a row was copied does not follow it,
/// and a row deleted in production is not deleted here — which is deliberate, so
/// that re-running this cannot undo work done on the target.
public static class SyncPlan
{
    public static readonly SyncKey[] Keys =
    [
        // The seeder creates system categories with its own ids, so a category
        // present on both sides is matched by name and its id remapped into every
        // Transaction and Rule that referenced production's.
        new("Categories", ["id"], NaturalKey: ["name"], RemapsIds: true),

        new("Users", ["id"], NaturalKey: ["email"]),

        // contentHash is the same SHA-256 of the same PDF on both sides, so a
        // statement re-uploaded to the target is recognised rather than doubled.
        new("StatementFiles", ["id"], NaturalKey: ["contentHash"], RemapsIds: true),

        new("Rules", ["id"], ForeignKeys: [new("categoryId", "Categories")]),
        new("RuleConditions", ["id"], ForeignKeys: [new("ruleId", "Rules")]),

        new("Transactions", ["id"],
            NaturalKey: ["externalId"],
            ForeignKeys: [new("categoryId", "Categories"), new("statementFileId", "StatementFiles")]),

        new("AmexTransactions", ["transactionId"],
            ForeignKeys: [new("statementFileId", "StatementFiles")]),

        new("BarclaysTransactions", ["transactionId"]),
        new("SantanderTransactions", ["transactionId"]),
        new("HsbcTransactions", ["transactionId"]),
        new("ChaseTransactions", ["transactionId"]),
        new("SofiTransactions", ["transactionId"]),

        new("MonzoApiTransactions", ["id"], NaturalKey: ["monzoId"]),
        new("MonzoRecRuns", ["id"]),

        new("InvestmentAccounts", ["id"], NaturalKey: ["owner", "name"], RemapsIds: true),
        new("InvestmentSnapshots", ["id"],
            NaturalKey: ["accountId", "date"],
            ForeignKeys: [new("accountId", "InvestmentAccounts")]),

        new("Tabs", ["id"]),
        new("Notes", ["id"]),
        new("RecurringVerdicts", ["id"], NaturalKey: ["owner", "description"]),
    ];

    /// Every move in MigrationPlan paired with its keys, in MigrationPlan's own
    /// order — which is foreign key order, and the order the load has to run in.
    public static IEnumerable<(TableMove Move, SyncKey Keys)> Ordered() =>
        MigrationPlan.Tables.Select(move => (move, Keys.Single(k => k.Target == move.Target)));

    /// Fails the run on drift rather than at the point a column silently goes
    /// missing. MigrationPlan is edited when a table changes; this file is the
    /// one that gets forgotten.
    public static void Validate()
    {
        var targets = MigrationPlan.Tables.Select(t => t.Target).ToHashSet(StringComparer.Ordinal);

        foreach (var key in Keys)
        {
            if (!targets.Contains(key.Target))
                throw new InvalidOperationException($"SyncPlan has [{key.Target}], MigrationPlan does not.");
        }

        foreach (var move in MigrationPlan.Tables)
        {
            var matches = Keys.Count(k => k.Target == move.Target);
            if (matches != 1)
                throw new InvalidOperationException($"[{move.Target}] has {matches} SyncPlan entries; expected exactly 1.");

            var keys = Keys.Single(k => k.Target == move.Target);
            var columns = move.Columns.ToHashSet(StringComparer.Ordinal);

            foreach (var column in keys.Key
                         .Concat(keys.NaturalKey ?? [])
                         .Concat((keys.ForeignKeys ?? []).Select(f => f.Column)))
            {
                if (!columns.Contains(column))
                    throw new InvalidOperationException(
                        $"[{move.Target}] keys on [{column}], which MigrationPlan does not copy.");
            }

            if (keys.RemapsIds && (keys.NaturalKey is null || !columns.Contains("id")))
                throw new InvalidOperationException(
                    $"[{move.Target}] remaps ids, which needs both an [id] column and a natural key to match on.");

            foreach (var foreignKey in keys.ForeignKeys ?? [])
            {
                if (!targets.Contains(foreignKey.References))
                    throw new InvalidOperationException(
                        $"[{move.Target}].[{foreignKey.Column}] references [{foreignKey.References}], which is not migrated.");
            }
        }
    }
}

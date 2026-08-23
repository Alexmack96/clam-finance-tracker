namespace Clam.Migrate;

/// One table's worth of the move, from Prisma's SQLite name to this schema's.
///
/// <param name="Source">The SQLite table. Prisma's names, so snake_case for most
/// and PascalCase for the four that predate that convention.</param>
/// <param name="Target">The SQL Server table. Plural, per db/schema.sql.</param>
/// <param name="Columns">Copied by name, identical on both sides. Listed rather
/// than discovered so a column added to one side and not the other fails here,
/// loudly, instead of being dropped in silence.</param>
/// <param name="SkipExistingIds">Read the target's ids first and skip rows that
/// already have one. Only Categories needs it, for the Uncategorised row
/// db/schema.sql ships.</param>
public sealed record TableMove(
    string Source,
    string Target,
    string[] Columns,
    bool SkipExistingIds = false);

/// What moves, and in what order.
///
/// **Order is foreign keys.** Categories before Transactions, StatementFiles
/// before the staging tables that point at them, InvestmentAccounts before their
/// snapshots. Getting it wrong is a constraint violation rather than silent
/// damage, which is the right way round.
///
/// **What is deliberately not here:**
///
///   * <c>session</c>, <c>account</c>, <c>verification</c> — Better Auth's, and
///     Better Auth is gone. WorkOS holds sessions now.
///   * <c>plaid_item</c>, <c>plaid_transaction</c> — the integration was deleted
///     unused and both tables are empty.
///   * <c>monzo_credential</c> — OAuth tokens tied to a redirect URI that has to
///     be re-registered against the new host anyway, so the connection is
///     re-authorised rather than carried across. Copying a token that will be
///     rejected only makes the failure later and stranger.
///
/// **The five staging tables with an IDENTITY id do not carry it across.** Their
/// <c>id</c> is a surrogate SQL Server assigns, and Prisma's autoincrement values
/// have no meaning worth preserving. One consequence is worth knowing: Barclays
/// and Santander rows that were processed before they had a content-hash
/// <c>transactionId</c> were normalised with an <c>externalId</c> built from that
/// surrogate — "barclays:41". Those Transactions keep their old externalId and
/// the staged row now has a different number. They are already processed, so
/// nothing re-reads the link; it is only wrong if you go looking.
public static class MigrationPlan
{
    public static readonly TableMove[] Tables =
    [
        // Uncategorised is already in the target: db/schema.sql inserts it with
        // production's own id, precisely so this step matches it rather than
        // colliding on the unique name.
        new("Category", "Categories", ["id", "name", "color"], SkipExistingIds: true),

        // workOsUserId is not in the source — Better Auth had no such column.
        // It is left null and filled in by --workos, or on first sign-in.
        new("user", "Users", ["id", "email", "name", "owner", "createdAt", "updatedAt", "image"]),

        new("statement_file", "StatementFiles",
            ["id", "bank", "owner", "statementDate", "originalName", "contentHash",
             "byteSize", "storageKey", "uploadedAt", "rowCount", "reconciled"]),

        new("Rule", "Rules",
            ["id", "kind", "position", "joinOperator", "bank", "categoryId", "bucket", "createdAt"]),

        new("RuleCondition", "RuleConditions",
            ["id", "ruleId", "field", "operator", "value", "negate", "position"]),

        // categoryPinned and bucketPinned are dropped: pinning was removed from
        // the model and the columns do not exist in the target.
        new("Transaction", "Transactions",
            ["id", "description", "amount", "type", "date", "createdAt", "categoryId",
             "externalId", "note", "owner", "reviewed", "bucket", "originalAmount",
             "originalCurrency", "statementFileId"]),

        new("amex_transaction", "AmexTransactions",
            ["transactionId", "transactionDate", "processDate", "description", "amount",
             "isCredit", "foreignCurrency", "foreignAmount", "statementDate", "owner",
             "importedAt", "status", "statementFileId"]),

        new("barclays_transaction", "BarclaysTransactions",
            ["transactionId", "date", "description", "amount", "isCredit",
             "statementDate", "owner", "importedAt", "status"]),

        new("santander_transaction", "SantanderTransactions",
            ["transactionId", "date", "description", "moneyIn", "moneyOut", "balance",
             "statementDate", "owner", "importedAt", "status"]),

        new("hsbc_transaction", "HsbcTransactions",
            ["transactionId", "date", "paymentType", "description", "moneyOut", "moneyIn",
             "balance", "statementDate", "owner", "importedAt", "status"]),

        new("chase_transaction", "ChaseTransactions",
            ["transactionId", "date", "description", "amount", "isCredit",
             "statementDate", "owner", "importedAt", "status"]),

        new("sofi_transaction", "SofiTransactions",
            ["transactionId", "date", "type", "description", "amount", "isCredit",
             "balance", "accountType", "statementDate", "owner", "importedAt", "status"]),

        new("monzo_api_transaction", "MonzoApiTransactions",
            ["id", "monzoId", "created", "settled", "amountPence", "currency",
             "localAmountPence", "localCurrency", "description", "notes", "monzoCategory",
             "merchantName", "merchantEmoji", "merchantAddress", "scheme",
             "includeInSpending", "accountId", "importedAt", "status"]),

        new("monzo_rec_run", "MonzoRecRuns",
            ["id", "ranAt", "window", "trigger", "totalMissing", "totalBackfilled", "results"]),

        new("investment_account", "InvestmentAccounts",
            ["id", "name", "category", "owner", "rate", "sortOrder", "createdAt", "updatedAt"]),

        new("investment_snapshot", "InvestmentSnapshots",
            ["id", "accountId", "date", "value", "createdAt", "updatedAt"]),

        new("tab", "Tabs",
            ["id", "person", "description", "amount", "direction", "status",
             "dueDate", "settledAt", "note", "createdAt", "updatedAt"]),

        new("note", "Notes", ["id", "title", "body", "pinned", "createdAt", "updatedAt"]),

        new("recurring_verdict", "RecurringVerdicts",
            ["id", "owner", "description", "status", "note", "createdAt", "updatedAt"]),
    ];
}

using Dapper;
using Microsoft.Data.SqlClient;

namespace Clam.Api.Tests;

/// The arrange half of every test, and the only place that writes to the
/// database directly.
///
/// One named method per scenario, so a test's first line reads as a sentence
/// about the world it needs rather than as twenty lines of INSERT. Tests assert
/// on exact snapshots, so nothing here is random and every value is chosen to
/// make one assertion legible.
///
/// Methods come in two kinds, and the distinction is load-bearing because the
/// database is shared across the whole run:
///
///   * **Worlds** reset the database first and describe it completely —
///     <see cref="NothingAsync"/>, <see cref="SeededAsync"/>,
///     <see cref="NotesAsync"/>. A test's first line is always one of these.
///   * **Additions** layer onto whichever world was just built —
///     <see cref="StagedAmexRowAsync"/>, <see cref="MonthlySubscriptionAsync"/>.
///     They never reset, and calling one without a world first is a bug that
///     shows up as a primary-key violation.
public sealed class Arrange(string connectionString, Action resetIds)
{
    // Ids are fixed and self-describing. They appear in snapshots, so a reader
    // should be able to tell what a row is without cross-referencing.
    public const string RentCategoryId = "ctest0000000000000001rent";
    public const string GroceriesCategoryId = "ctest000000000000002groc";
    public const string NetCategoryId = "ctest00000000000000003net";
    public const string SalaryCategoryId = "ctest0000000000000004sal";
    public const string UnusedCategoryId = "ctest000000000000005none";
    public const string UncategorisedCategoryId = "ctest00000000000006uncat";

    public const string RentTransactionId = "ctest00000000000000001tx";
    public const string GroceriesTransactionId = "ctest00000000000000002tx";
    public const string SettlementTransactionId = "ctest00000000000000003tx";
    public const string SalaryTransactionId = "ctest00000000000000004tx";

    /// Chosen so the dashboard's settlement lands on a value that could not
    /// appear by accident: 600.00 - (1200.50 / 2) = -0.25.
    public const decimal CaseyIn = 600.00m;
    public const decimal RentAmount = 1000.50m;
    public const decimal GroceriesAmount = 200.00m;

    /// Inside the frozen clock's year, so the year-to-date endpoints see them.
    public static readonly DateTime RentDate = new(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime GroceriesDate = new(2026, 2, 14, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime SettlementDate = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime SalaryDate = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    /// The frozen clock's month, for the endpoints that only look at "now".
    public static readonly DateTime ThisMonth = new(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);

    private const string InsertCategorySql =
        "INSERT INTO [Categories] ([id], [name], [color]) VALUES (@Id, @Name, @Color);";

    private const string InsertTransactionSql = """
        INSERT INTO [Transactions]
            ([id], [description], [amount], [type], [date], [createdAt], [categoryId],
             [externalId], [owner], [reviewed], [bucket])
        VALUES
            (@Id, @Description, @Amount, @Type, @Date, @Date, @CategoryId,
             @ExternalId, @Owner, @Reviewed, @Bucket);
        """;

    /// Truncation order is parent-last. Rules are cleared explicitly rather than
    /// left to the cascade from Categories: a Bucket rule has a null categoryId,
    /// so nothing cascades to it and it would survive into the next test as a
    /// rule that silently rewrites buckets during an unrelated assertion.
    private const string ClearSql = """
        DELETE FROM [RuleConditions];
        DELETE FROM [Rules];
        DELETE FROM [RecurringVerdicts];
        DELETE FROM [InvestmentSnapshots];
        DELETE FROM [InvestmentAccounts];
        DELETE FROM [Notes];
        DELETE FROM [Tabs];
        DELETE FROM [MonzoApiTransactions];
        DELETE FROM [MonzoRecRuns];
        DELETE FROM [AmexTransactions];
        DELETE FROM [BarclaysTransactions];
        DELETE FROM [SantanderTransactions];
        DELETE FROM [HsbcTransactions];
        DELETE FROM [SofiTransactions];
        DELETE FROM [ChaseTransactions];
        DELETE FROM [Transactions];
        DELETE FROM [StatementFiles];
        DELETE FROM [Categories];
        DELETE FROM [Accounts];
        DELETE FROM [Sessions];
        DELETE FROM [Users];
        DELETE FROM [MonzoCredentials];
        DELETE FROM [Verifications];
        """;

    private async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private Task ExecuteAsync(string sql, object? parameters = null) => ExecuteCoreAsync(sql, parameters);

    private async Task ExecuteCoreAsync(string sql, object? parameters)
    {
        await using var connection = await OpenAsync();
        await connection.ExecuteAsync(new CommandDefinition(
            sql, parameters, cancellationToken: TestContext.Current.CancellationToken));
    }

    /// An empty database. The starting point for every test — nothing carries
    /// over from whatever ran before.
    ///
    /// That includes the generated-id counter. It is a singleton on a host
    /// shared by the whole assembly, so resetting the rows without resetting the
    /// numbering leaves ids that depend on execution order.
    public Task NothingAsync()
    {
        resetIds();
        return ExecuteAsync(ClearSql);
    }

    /// Five categories and four transactions: two Joint expenses, Casey's
    /// settlement income and Alex's salary. The baseline most tests read from.
    public async Task SeededAsync()
    {
        await NothingAsync();

        await ExecuteAsync(InsertCategorySql, new[]
        {
            new { Id = GroceriesCategoryId, Name = "Groceries", Color = "#84cc16" },
            new { Id = NetCategoryId, Name = "Net", Color = "#3b82f6" },
            new { Id = RentCategoryId, Name = "Rent", Color = "#ef4444" },
            new { Id = SalaryCategoryId, Name = "Salary", Color = "#16a34a" },
            // Referenced by nothing. Exists so the categories endpoint has a row
            // with transactionCount 0 to prove its LEFT JOIN is not an INNER one.
            new { Id = UnusedCategoryId, Name = "Unused", Color = "#000000" },
        });

        await ExecuteAsync(InsertTransactionSql, new[]
        {
            new
            {
                Id = RentTransactionId,
                Description = "RENT PAYMENT",
                Amount = RentAmount,
                Type = "Expense",
                Date = RentDate,
                CategoryId = RentCategoryId,
                ExternalId = (string?)null,
                Owner = "Joint",
                Reviewed = true,
                Bucket = (string?)"Needs",
            },
            new
            {
                Id = GroceriesTransactionId,
                Description = "TESCO STORES",
                Amount = GroceriesAmount,
                Type = "Expense",
                Date = GroceriesDate,
                CategoryId = GroceriesCategoryId,
                // A second row with a NULL externalId. Together with the row
                // above this is what would break under a plain UNIQUE constraint
                // instead of the schema's filtered index.
                ExternalId = (string?)null,
                Owner = "Joint",
                Reviewed = false,
                Bucket = (string?)"Needs",
            },
            new
            {
                Id = SettlementTransactionId,
                Description = "MONZO TRANSFER FROM CASEY",
                Amount = CaseyIn,
                Type = "Income",
                Date = SettlementDate,
                CategoryId = NetCategoryId,
                // The dashboard only counts Casey's Net income when it looks like
                // it came from the Monzo sync. Drop this prefix and caseyIn is 0.
                ExternalId = (string?)"monzo:tx_casey_settlement",
                Owner = "Casey",
                Reviewed = true,
                Bucket = (string?)"Ignore",
            },
            new
            {
                Id = SalaryTransactionId,
                Description = "ACME LTD SALARY",
                Amount = 3000.00m,
                Type = "Income",
                Date = SalaryDate,
                CategoryId = SalaryCategoryId,
                ExternalId = (string?)"monzo:tx_alex_salary",
                Owner = "Alex",
                Reviewed = true,
                Bucket = (string?)"Ignore",
            },
        });
    }

    /// The seeded set plus the Uncategorised category. The import pipeline falls
    /// back to it, and refuses to run without it.
    public async Task SeededWithUncategorisedAsync()
    {
        await SeededAsync();
        await ExecuteAsync(InsertCategorySql,
            new { Id = UncategorisedCategoryId, Name = "Uncategorised", Color = "#d1d5db" });
    }

    /// A category rule (TESCO → Rent) and a bucket rule (category Rent → Wants),
    /// which together exercise the two-pass pipeline.
    public async Task SeededWithRulesAsync()
    {
        await SeededWithUncategorisedAsync();

        await ExecuteAsync("""
            INSERT INTO [Rules] ([id], [kind], [position], [joinOperator], [categoryId], [createdAt])
            VALUES ('crule0000000000000001cat', 'Category', 0, 'AND', @RentCategoryId, @CreatedAt);

            INSERT INTO [RuleConditions] ([id], [ruleId], [field], [operator], [value], [negate], [position])
            VALUES ('ccond0000000000000001cat', 'crule0000000000000001cat', 'Description', 'Contains', 'TESCO', 0, 0);

            INSERT INTO [Rules] ([id], [kind], [position], [joinOperator], [bucket], [createdAt])
            VALUES ('crule000000000000001bkt', 'Bucket', 0, 'AND', 'Wants', @CreatedAt);

            INSERT INTO [RuleConditions] ([id], [ruleId], [field], [operator], [value], [negate], [position])
            VALUES ('ccond000000000000001bkt', 'crule000000000000001bkt', 'Category', 'Exact', 'Rent', 0, 0);
            """,
            new { RentCategoryId, CreatedAt = ClamApiFactory.Now.UtcDateTime });
    }

    /// One pending Amex row, ready for the process step.
    public async Task StagedAmexRowAsync(string transactionId = "amex_row_1", string amount = "12.34")
    {
        await ExecuteAsync("""
            INSERT INTO [AmexTransactions]
                ([transactionId], [transactionDate], [processDate], [description], [amount],
                 [isCredit], [statementDate], [owner])
            VALUES (@TransactionId, '2026-02-14', '2026-02-15', 'AMAZON UK', @Amount,
                    0, 'February 2026', 'Alex');
            """,
            new { TransactionId = transactionId, Amount = amount });
    }

    /// One Barclays credit and one Barclays debit. The credit must be skipped —
    /// it is a payment to the card from an account already imported.
    public Task StagedBarclaysPairAsync() => ExecuteAsync("""
        INSERT INTO [BarclaysTransactions]
            ([transactionId], [date], [description], [amount], [isCredit], [statementDate], [owner])
        VALUES ('barclays_credit', '2026-02-10', 'PAYMENT RECEIVED', '100.00', 1, 'Feb 2026', 'Alex'),
               ('barclays_debit',  '2026-02-14', 'SAINSBURYS',       '25.00',  0, 'Feb 2026', 'Alex');
        """);

    /// One pending SoFi row in dollars, which the process step has to convert.
    public Task StagedSofiRowAsync() => ExecuteAsync("""
        INSERT INTO [SofiTransactions]
            ([transactionId], [date], [type], [description], [amount], [isCredit], [statementDate], [owner])
        VALUES ('sofi_row_1', '2026-02-14', 'Purchase', 'TARGET', '50.00', 0, 'Feb 2026', 'Casey');
        """);

    /// Six monthly charges on one description. Detection is a timing test rather
    /// than an amount test, so the amounts vary on purpose — a variable bill has
    /// to be detected too.
    public async Task MonthlySubscriptionAsync(string description = "NETFLIX.COM")
    {
        var start = new DateTime(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc);

        await ExecuteAsync(InsertTransactionSql, Enumerable.Range(0, 6).Select(i => new
        {
            Id = $"crecur0000000000000{i:D5}",
            Description = description,
            Amount = 10.99m + i,
            Type = "Expense",
            Date = start.AddDays(i * 30),
            CategoryId = GroceriesCategoryId,
            ExternalId = (string?)$"monzo:tx_sub_{i}",
            Owner = "Alex",
            Reviewed = false,
            Bucket = (string?)"Wants",
        }).ToArray());
    }

    /// Rent, Water and Wifi categories with payments dated inside the frozen
    /// clock's month, which is the only month the utilities page looks at.
    public async Task UtilitiesThisMonthAsync()
    {
        await NothingAsync();

        await ExecuteAsync(InsertCategorySql, new[]
        {
            new { Id = RentCategoryId, Name = "Rent", Color = "#ef4444" },
            new { Id = "ctest00000000000006water", Name = "Water", Color = "#0ea5e9" },
            new { Id = "ctest00000000000007wifi", Name = "Wifi", Color = "#8b5cf6" },
        });

        await ExecuteAsync(InsertTransactionSql, new[]
        {
            new
            {
                Id = "cutil00000000000000001tx",
                Description = "RENT",
                Amount = 1400.00m,
                Type = "Expense",
                Date = ThisMonth,
                CategoryId = RentCategoryId,
                ExternalId = (string?)null,
                Owner = "Joint",
                Reviewed = false,
                Bucket = (string?)"Needs",
            },
            new
            {
                Id = "cutil00000000000000002tx",
                Description = "THAMES WATER",
                Amount = 32.10m,
                Type = "Expense",
                Date = ThisMonth,
                CategoryId = "ctest00000000000006water",
                ExternalId = (string?)null,
                Owner = "Alex",
                Reviewed = false,
                Bucket = (string?)"Needs",
            },
        });
    }

    /// Two investment accounts and a pension, snapshotted on two dates. Enough
    /// for every NAV figure to be a different number.
    public async Task InvestmentHistoryAsync()
    {
        await NothingAsync();

        await ExecuteAsync("""
            INSERT INTO [InvestmentAccounts] ([id], [name], [category], [owner], [rate], [sortOrder])
            VALUES ('cacct0000000000001equity', 'Vanguard',    'equity',  'Alex', NULL, 1),
                   ('cacct00000000000002cash',  'Chase Saver', 'cash',    'Alex', 4.5,  2),
                   ('cacct0000000000003pension','Aviva',       'pension', 'Alex', NULL, 3);

            INSERT INTO [InvestmentSnapshots] ([id], [accountId], [date], [value])
            VALUES ('csnap0000000000000000001', 'cacct0000000000001equity', '2026-01-31', 1000),
                   ('csnap0000000000000000002', 'cacct00000000000002cash',  '2026-01-31', 500),
                   ('csnap0000000000000000003', 'cacct0000000000003pension','2026-01-31', 9000),
                   ('csnap0000000000000000004', 'cacct0000000000001equity', '2026-02-28', 1200),
                   ('csnap0000000000000000005', 'cacct00000000000002cash',  '2026-02-28', 400),
                   ('csnap0000000000000000006', 'cacct0000000000003pension','2026-02-28', 9500);
            """);
    }

    /// A world: a statement file with two staged rows against it — the shape the
    /// detail and delete slices report on.
    public async Task StatementWithRowsAsync()
    {
        await NothingAsync();
        await InsertStatementAsync();
    }

    private Task InsertStatementAsync() => ExecuteAsync("""
        INSERT INTO [StatementFiles]
            ([id], [bank], [owner], [statementDate], [originalName], [contentHash],
             [byteSize], [storageKey], [rowCount], [reconciled])
        VALUES ('cstmt0000000000000000001', 'amex', 'Alex', 'February 2026', 'amex-feb.pdf',
                'a1b2c3d4e5f6', 20480, 'amex/Alex/February-2026-a1b2c3d4e5f6.pdf', 2, 1);

        INSERT INTO [AmexTransactions]
            ([transactionId], [transactionDate], [processDate], [description], [amount],
             [isCredit], [statementDate], [owner], [status], [statementFileId])
        VALUES ('amex_stmt_1', '2026-02-10', '2026-02-11', 'PRET A MANGER', '4.85', 0,
                'February 2026', 'Alex', 'processed', 'cstmt0000000000000000001'),
               ('amex_stmt_2', '2026-02-12', '2026-02-13', 'UBER TRIP',     '9.20', 0,
                'February 2026', 'Alex', 'pending',   'cstmt0000000000000000001');
        """);

    public const string PinnedNoteId = "cnote0000000000000pinned";
    public const string PlainNoteId = "cnote00000000000000plain";

    /// A world: one pinned note and one not, so list order is observable.
    public async Task NotesAsync()
    {
        await NothingAsync();
        await InsertNotesAsync();
    }

    private Task InsertNotesAsync() => ExecuteAsync("""
        INSERT INTO [Notes] ([id], [title], [body], [pinned], [createdAt], [updatedAt])
        VALUES (@PlainNoteId,  'Chase the Amex statement', 'February is missing', 0, @Older, @Older),
               (@PinnedNoteId, 'Rules to rewrite',         NULL,                  1, @Newer, @Newer);
        """,
        new
        {
            PlainNoteId,
            PinnedNoteId,
            Older = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc),
            Newer = new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc),
        });

    public const string OpenTabId = "ctab00000000000000000open";
    public const string SettledTabId = "ctab000000000000000settl";

    /// A world: two open tabs in opposite directions and one already settled,
    /// which is what makes the totals worth asserting.
    public async Task TabsAsync()
    {
        await NothingAsync();
        await InsertTabsAsync();
    }

    private Task InsertTabsAsync() => ExecuteAsync("""
        INSERT INTO [Tabs] ([id], [person], [description], [amount], [direction], [status], [settledAt],
                            [createdAt], [updatedAt])
        VALUES (@OpenTabId,    'Sam', 'dinner',  20.50, 'TheyOwe', 'Open',    NULL,     @Created, @Created),
               ('ctab0000000000000000owe', 'Jo', 'taxi', 10.00, 'IOwe',  'Open',    NULL,     @Created, @Created),
               (@SettledTabId, 'Kit', 'cinema',  99.00, 'TheyOwe', 'Settled', @Created, @Created, @Created);
        """,
        new { OpenTabId, SettledTabId, Created = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc) });

    /// A world: one user with a connected Monzo account whose token is still
    /// valid.
    public async Task MonzoConnectedAsync()
    {
        await NothingAsync();
        await InsertMonzoCredentialAsync();
    }

    private Task InsertMonzoCredentialAsync() => ExecuteAsync("""
        INSERT INTO [Users] ([id], [email], [name])
        VALUES ('cuser0000000000000000001', 'alex@example.com', 'Alex');

        INSERT INTO [MonzoCredentials] ([id], [userId], [accessToken], [refreshToken], [expiresAt], [accountId])
        VALUES ('ccred0000000000000000001', 'cuser0000000000000000001',
                'access_test', 'refresh_test', @ExpiresAt, 'acc_retail');
        """,
        new { ExpiresAt = ClamApiFactory.Now.UtcDateTime.AddHours(6) });
}

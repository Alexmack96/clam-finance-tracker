using Dapper;
using Microsoft.Data.SqlClient;

namespace Clam.Api.Tests;

/// A small, hand-written, fully known dataset — the opposite of the Bogus-driven
/// dev seeder, on purpose. Tests assert exact totals, so every row here is
/// chosen to make one assertion meaningful and nothing is random.
///
/// The numbers are picked so the dashboard's settlement lands on a value that
/// could not appear by accident: caseyIn 600.00 - (jointExpenses 1200.50 / 2)
/// = -0.25.
internal static class TestDataSeeder
{
    internal const string RentCategoryId = "ctest0000000000000001rent";
    internal const string GroceriesCategoryId = "ctest000000000000002groc";
    internal const string NetCategoryId = "ctest00000000000000003net";
    internal const string SalaryCategoryId = "ctest0000000000000004sal";
    internal const string UnusedCategoryId = "ctest000000000000005none";

    internal const decimal CaseyIn = 600.00m;
    internal const decimal JointExpenses = 1200.50m;
    internal const decimal Settlement = CaseyIn - (JointExpenses / 2);   // -0.25

    /// One Joint expense sits in Rent and one in Groceries, so the pie chart has
    /// a deterministic order (Rent first, it is larger).
    internal const decimal RentAmount = 1000.50m;
    internal const decimal GroceriesAmount = 200.00m;

    internal static readonly DateTime NewestDate = new(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
    internal static readonly DateTime OldestDate = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    private const string InsertCategorySql =
        "INSERT INTO [Category] ([id], [name], [color]) VALUES (@Id, @Name, @Color);";

    private const string InsertTransactionSql = """
        INSERT INTO [Transaction]
            ([id], [description], [amount], [type], [date], [createdAt], [categoryId],
             [externalId], [owner], [reviewed], [bucket])
        VALUES
            (@Id, @Description, @Amount, @Type, @Date, @Date, @CategoryId,
             @ExternalId, @Owner, @Reviewed, @Bucket);
        """;

    /// Idempotent — clears both tables first.
    ///
    /// It has to be. A FastEndpoints AppFixture instance is shared by every test
    /// class that uses it, and SetupAsync is invoked once per class, so this runs
    /// several times against the same database in a single run. Insert-only would
    /// throw a primary key violation on the second class.
    internal static async Task SeedAsync(string connectionString, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        // Children first — the FK forbids clearing Category while rows reference it.
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM [Transaction]; DELETE FROM [Category];", cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(InsertCategorySql, new[]
        {
            new { Id = GroceriesCategoryId, Name = "Groceries", Color = "#84cc16" },
            new { Id = NetCategoryId, Name = "Net", Color = "#3b82f6" },
            new { Id = RentCategoryId, Name = "Rent", Color = "#ef4444" },
            new { Id = SalaryCategoryId, Name = "Salary", Color = "#16a34a" },
            // Referenced by nothing. Exists so the categories endpoint has a row
            // with transactionCount 0 to prove its LEFT JOIN is not an INNER one.
            new { Id = UnusedCategoryId, Name = "Unused", Color = "#000000" },
        }, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(InsertTransactionSql, new[]
        {
            new
            {
                Id = "ctest00000000000000001tx",
                Description = "RENT PAYMENT",
                Amount = RentAmount,
                Type = "Expense",
                Date = NewestDate,
                CategoryId = RentCategoryId,
                ExternalId = (string?)null,
                Owner = "Joint",
                Reviewed = true,
                Bucket = (string?)"Needs",
            },
            new
            {
                Id = "ctest00000000000000002tx",
                Description = "TESCO STORES",
                Amount = GroceriesAmount,
                Type = "Expense",
                Date = new DateTime(2026, 2, 14, 0, 0, 0, DateTimeKind.Utc),
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
                Id = "ctest00000000000000003tx",
                Description = "MONZO TRANSFER FROM CASEY",
                Amount = CaseyIn,
                Type = "Income",
                Date = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
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
                Id = "ctest00000000000000004tx",
                Description = "ACME LTD SALARY",
                Amount = 3000.00m,
                Type = "Income",
                Date = OldestDate,
                CategoryId = SalaryCategoryId,
                ExternalId = (string?)"monzo:tx_alex_salary",
                Owner = "Alex",
                Reviewed = true,
                Bucket = (string?)"Ignore",
            },
        }, cancellationToken: ct));
    }

    /// Used by the seed-endpoint tests to make the database look like it holds a
    /// real bank import, which is what SeedDataCommand refuses to overwrite.
    internal static async Task InsertRealBankRowAsync(string connectionString, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(InsertTransactionSql, new
        {
            Id = "ctest0000000000000amex01",
            Description = "AMEX IMPORTED ROW",
            Amount = 42.00m,
            Type = "Expense",
            Date = NewestDate,
            CategoryId = RentCategoryId,
            ExternalId = (string?)"amex:ref_realimport",
            Owner = "Alex",
            Reviewed = false,
            Bucket = (string?)"Needs",
        }, cancellationToken: ct));
    }
}

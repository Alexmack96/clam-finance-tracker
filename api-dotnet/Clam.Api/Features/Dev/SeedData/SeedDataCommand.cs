using Ardalis.Result;
using Bogus;
using Clam.Api.Domain;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Dev.SeedData;

/// Generates synthetic data only. Nothing here is copied from the production
/// database, and nothing should be: this service is unauthenticated while it is
/// a read-only spike, so "realistic" has to mean shaped like the real thing, not
/// derived from it.
public sealed class SeedDataCommand(IDbConnectionFactory factory)
{
    private const string DeleteSql = "DELETE FROM [Transactions]; DELETE FROM [Categories];";

    /// The seeder only ever writes `monzo:` external ids (see below), so a row
    /// carrying any other bank's namespace came from a real import. Finding one
    /// means this is pointed at a database with real data in it, and the next
    /// statement would be a DELETE of all of it.
    private const string RealDataProbeSql = """
        SELECT TOP 1 [externalId]
        FROM   [Transactions]
        WHERE  [externalId] IS NOT NULL
          AND  [externalId] NOT LIKE 'monzo:%';
        """;

    private const string InsertCategorySql =
        "INSERT INTO [Categories] ([id], [name], [color]) VALUES (@Id, @Name, @Color);";

    private const string InsertTransactionSql = """
        INSERT INTO [Transactions]
            ([id], [description], [amount], [type], [date], [createdAt], [categoryId],
             [externalId], [note], [owner], [reviewed], [bucket], [categoryPinned],
             [bucketPinned], [originalAmount], [originalCurrency], [statementFileId])
        VALUES
            (@id, @description, @amount, @type, @date, @createdAt, @categoryId,
             @externalId, @note, @owner, @reviewed, @bucket, @categoryPinned,
             @bucketPinned, @originalAmount, @originalCurrency, @statementFileId);
        """;

    public async Task<Result<SeedDataResponse>> ExecuteAsync(int transactionCount, CancellationToken ct)
    {
        // Fixed seed: a reseed must produce identical data, otherwise a parity
        // diff against the Express API is comparing two moving targets.
        var faker = new Faker { Random = new Randomizer(20260808) };

        var categories = SeedCategoryCatalog.All
            .Select(c => new { Definition = c, Id = NewCuid(faker) })
            .ToList();

        var yearStart = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // Clamped to now, not to midnight-plus-a-random-offset: a transaction
        // dated later today reads as a bug to anyone looking at the UI.
        var latest = DateTime.UtcNow;
        var span = (latest - yearStart).TotalMinutes;

        var transactions = new List<object>(transactionCount);

        for (var i = 0; i < transactionCount; i++)
        {
            var picked = faker.PickRandom(categories);
            var category = picked.Definition;
            var isIncome = category.Name is SeedCategoryCatalog.IncomeCategory
                                         or SeedCategoryCatalog.SettlementCategory;
            var owner = PickOwner(faker, category.Name);
            var date = yearStart.AddMinutes(faker.Random.Double(0, span));

            // Casey's "Net" income is what the dashboard settlement is computed
            // from, and it only counts when the row looks like it came from the
            // Monzo sync. Those rows therefore always get a monzo: external id.
            var isSettlementRow = category.Name == SeedCategoryCatalog.SettlementCategory
                                  && owner == Owner.Casey;

            // Most rows have no external id at all — which is also what exercises
            // the filtered unique index. A plain UNIQUE constraint would reject
            // the second NULL here and the seed would fail on row two.
            var externalId = isSettlementRow || faker.Random.Bool(0.35f)
                ? $"monzo:tx_{faker.Random.AlphaNumeric(18)}"
                : null;

            transactions.Add(new
            {
                id = NewCuid(faker),
                description = Describe(faker, category.Name),
                amount = Math.Round(faker.Random.Decimal(category.Low, category.High), 2),
                type = (isIncome ? TransactionType.Income : TransactionType.Expense).ToString(),
                date,
                createdAt = date,
                categoryId = picked.Id,
                externalId,
                note = faker.Random.Bool(0.08f) ? faker.Lorem.Sentence(4) : null,
                owner = owner.ToString(),
                reviewed = faker.Random.Bool(0.6f),
                bucket = category.Bucket.ToString(),
                categoryPinned = faker.Random.Bool(0.1f),
                bucketPinned = faker.Random.Bool(0.1f),
                originalAmount = (decimal?)null,
                originalCurrency = (string?)null,
                statementFileId = (string?)null,
            });
        }

        using var connection = await factory.OpenAsync(ct);

        // Checked before the transaction opens, because the answer is "refuse",
        // not "roll back". This is the one thing that can go wrong here that is
        // not an exception: an operator pointing a destructive dev tool at a
        // database holding real imports. A Result says so; throwing would turn a
        // correct refusal into a 500.
        var foreignExternalId = await connection.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(RealDataProbeSql, cancellationToken: ct));

        if (foreignExternalId is not null)
        {
            return Result<SeedDataResponse>.Conflict(
                $"Refusing to seed: this database holds transactions from a real bank import " +
                $"(found externalId '{foreignExternalId}'). Seeding deletes every row in Transactions and Categories.");
        }

        using var tx = connection.BeginTransaction();

        // Children first — the FK forbids clearing Categories while rows reference it.
        await connection.ExecuteAsync(DeleteSql, transaction: tx);

        await connection.ExecuteAsync(
            InsertCategorySql,
            categories.Select(c => new { c.Id, c.Definition.Name, c.Definition.Color }),
            transaction: tx);

        // Dapper turns an IEnumerable of parameters into one round trip per row.
        // Fine for a few thousand seed rows; SqlBulkCopy is the answer if this
        // ever needs to be fast.
        await connection.ExecuteAsync(InsertTransactionSql, transactions, transaction: tx);

        tx.Commit();

        return new SeedDataResponse { Categories = categories.Count, Transactions = transactions.Count };
    }

    private static Owner PickOwner(Faker faker, string categoryName) => categoryName switch
    {
        // Rent and utilities are shared, so they drive the Joint expense total.
        "Rent" or "Utilities" or "Groceries" => Owner.Joint,
        SeedCategoryCatalog.SettlementCategory => Owner.Casey,
        _ => faker.Random.WeightedRandom([Owner.Alex, Owner.Casey, Owner.Joint], [0.4f, 0.3f, 0.3f]),
    };

    private static string Describe(Faker faker, string categoryName) => categoryName switch
    {
        SeedCategoryCatalog.IncomeCategory => "ACME LTD SALARY",
        SeedCategoryCatalog.SettlementCategory => "MONZO TRANSFER FROM CASEY",
        "Rent" => "RENT PAYMENT",
        "Utilities" => faker.PickRandom("OCTOPUS ENERGY", "THAMES WATER", "COUNCIL TAX"),
        "Groceries" => faker.PickRandom("TESCO STORES", "SAINSBURYS", "ALDI", "M&S SIMPLY FOOD"),
        "Takeout" => faker.PickRandom("DELIVEROO", "UBER EATS", "JUST EAT"),
        "Transport" => faker.PickRandom("TFL TRAVEL", "TRAINLINE", "UBER TRIP"),
        "Subscriptions" => faker.PickRandom("NETFLIX", "SPOTIFY", "GITHUB", "ICLOUD"),
        "Golf" => faker.PickRandom("AMERICAN GOLF", "GREEN FEES", "DRIVING RANGE"),
        _ => faker.Company.CompanyName().ToUpperInvariant(),
    };

    /// Prisma ids are cuids. Nothing depends on the algorithm, only on the shape —
    /// a 25-character lowercase string that fits the NVARCHAR(30) columns.
    private static string NewCuid(Faker faker)
        => $"c{faker.Random.String2(24, "abcdefghijklmnopqrstuvwxyz0123456789")}";
}

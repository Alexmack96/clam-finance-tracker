using Clam.Api.Domain;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Categories;

/// Brings a database up to the system category set on boot, and seeds each new
/// category's starting Bucket rule.
///
/// This is a port of the Express service's <c>initSystemCategories</c>, which
/// ran on every listen. Without it a freshly created database comes up with one
/// category (<c>Uncategorised</c>, which <c>db/schema.sql</c> inserts because
/// the import pipeline throws without it) and no bucket rules at all — so every
/// import lands uncategorised and the dashboard's Needs/Wants/Savings split is
/// three zeroes. Nothing fails; it just quietly has no data.
///
/// **Create-only, never update.** A category that already exists is skipped
/// whole, colour included, because by then it belongs to the user rather than to
/// this list. The bucket rule is created *only* alongside a new category, which
/// is what stops a re-run resurrecting a rule that was deleted on purpose.
///
/// **The starting bucket is a real rule, not a column**, so bucketing is decided
/// in one place — the rules engine — and the seeded rule can be reordered or
/// deleted like any other.
///
/// It runs as a background service rather than inline before <c>app.Run()</c>.
/// A cold Azure SQL can take tens of seconds to answer its first query, and
/// blocking startup on that is how a container fails its health probe and gets
/// restarted into a loop. The API serves traffic immediately; this catches up
/// behind it.
public sealed class SystemCategorySeeder(
    IDbConnectionFactory factory,
    IIdGenerator ids,
    TimeProvider clock,
    ILogger<SystemCategorySeeder> logger) : BackgroundService
{
    private const string ExistingNamesSql = "SELECT [name] FROM [Categories];";

    private const string InsertCategorySql =
        "INSERT INTO [Categories] ([id], [name], [color]) VALUES (@Id, @Name, @Color);";

    /// The next free Bucket-rule position. Read per insert rather than once up
    /// front: the rules are ordered and first match wins, so a seeded rule has
    /// to land after every rule that already exists — including the ones this
    /// same loop just added.
    private const string NextBucketPositionSql =
        "SELECT COALESCE(MAX([position]), -1) + 1 FROM [Rules] WHERE [kind] = 'Bucket';";

    private const string InsertRuleSql = """
        INSERT INTO [Rules] ([id], [kind], [position], [joinOperator], [bucket], [createdAt])
        VALUES (@Id, 'Bucket', @Position, 'AND', @Bucket, @CreatedAt);

        INSERT INTO [RuleConditions] ([id], [ruleId], [field], [operator], [value], [negate], [position])
        VALUES (@ConditionId, @Id, 'Category', 'Exact', @Name, 0, 0);
        """;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SeedAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is shutting down. Nothing to report and nothing half-done:
            // each category is its own transaction.
        }
        catch (Exception ex)
        {
            // Never fatal. A database that cannot be reached at boot is a reason
            // to log and carry on, not to take the API down with it — the app is
            // still able to serve every read that does not need these rows.
            logger.LogError(ex, "System category seeding failed outright");
        }
    }

    /// Public so the tests can drive it directly. Running it through the host
    /// instead would mean racing a background task against Respawn, which wipes
    /// the database between tests — the seeder would be inserting rows into
    /// whichever test happened to be running when it caught up.
    public async Task SeedAsync(CancellationToken ct)
    {
        using var connection = await factory.OpenAsync(ct);

        var existing = (await connection.QueryAsync<string>(
            new CommandDefinition(ExistingNamesSql, cancellationToken: ct)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = 0;

        foreach (var category in SystemCategories.All)
        {
            if (existing.Contains(category.Name)) continue;

            try
            {
                // One transaction per category, so a failure on one leaves the
                // others done rather than rolling back the whole set. That
                // matches the Express original, which caught per category.
                using var transaction = connection.BeginTransaction();

                var categoryId = ids.NewId();
                await connection.ExecuteAsync(new CommandDefinition(
                    InsertCategorySql,
                    new { Id = categoryId, category.Name, category.Color },
                    transaction,
                    cancellationToken: ct));

                if (category.Bucket is { } bucket)
                {
                    var position = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                        NextBucketPositionSql, transaction: transaction, cancellationToken: ct));

                    await connection.ExecuteAsync(new CommandDefinition(
                        InsertRuleSql,
                        new
                        {
                            Id = ids.NewId(),
                            ConditionId = ids.NewId(),
                            Position = position,
                            Bucket = bucket.ToString(),
                            category.Name,
                            CreatedAt = clock.GetUtcNow().UtcDateTime,
                        },
                        transaction,
                        cancellationToken: ct));
                }

                transaction.Commit();
                created++;
            }
            catch (Exception ex)
            {
                // Two instances booting at once both see the category missing and
                // both insert it; the unique index on [name] means one of them
                // loses. That is the expected outcome, not a fault, and the loser
                // has nothing left to do.
                logger.LogWarning(ex, "Could not seed system category {Category}", category.Name);
            }
        }

        if (created > 0) logger.LogInformation("Seeded {Count} system categories", created);
    }
}

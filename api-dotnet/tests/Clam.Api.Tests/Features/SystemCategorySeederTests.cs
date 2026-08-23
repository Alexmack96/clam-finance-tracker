using Clam.Api.Features.Categories;
using Clam.Api.Infrastructure.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Clam.Api.Tests.Features;

/// The boot-time system category seeder, driven directly rather than through the
/// host — see the note on <see cref="SystemCategorySeeder.SeedAsync"/> for why.
public class SystemCategorySeederTests(ClamApiFactory api) : ApiTest(api)
{
    private SystemCategorySeeder Seeder() => new(
        new SqlConnectionFactory(Api.ConnectionString),
        Api.Ids,
        new FakeTimeProvider(new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.Zero)),
        NullLogger<SystemCategorySeeder>.Instance);

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(Api.ConnectionString);
        return await connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, cancellationToken: Ct));
    }

    /// A database with nothing but the Uncategorised row schema.sql ships comes
    /// up with the full set, and every bucketed category gets its rule.
    [Fact]
    public async Task Seeds_the_system_categories_and_their_bucket_rules()
    {
        await Given.NothingAsync();

        await Seeder().SeedAsync(Ct);

        var response = await Get("/api/categories");
        await Verify(response);
    }

    /// The rules the seeder created, which are the half that is easy to get
    /// wrong: each is a Bucket rule matching its own category by exact name, and
    /// their positions have to be contiguous from zero or the ordering that
    /// decides "first match wins" is not what it looks like.
    [Fact]
    public async Task Seeds_one_bucket_rule_per_bucketed_category()
    {
        await Given.NothingAsync();

        await Seeder().SeedAsync(Ct);

        var response = await Get("/api/rules");
        await Verify(response);
    }

    /// Running twice changes nothing. This is the property that matters most:
    /// the seeder runs on every boot, so a non-idempotent one would add a
    /// duplicate rule per restart until the rules list was unusable.
    [Fact]
    public async Task Running_twice_adds_nothing_the_second_time()
    {
        await Given.NothingAsync();

        await Seeder().SeedAsync(Ct);
        var afterFirst = await ScalarAsync<int>("SELECT COUNT(*) FROM [Categories];");
        var rulesAfterFirst = await ScalarAsync<int>("SELECT COUNT(*) FROM [Rules];");

        await Seeder().SeedAsync(Ct);

        Assert.Equal(afterFirst, await ScalarAsync<int>("SELECT COUNT(*) FROM [Categories];"));
        Assert.Equal(rulesAfterFirst, await ScalarAsync<int>("SELECT COUNT(*) FROM [Rules];"));
    }

    /// A category the user already has is left entirely alone — including its
    /// colour, which by then is their choice and not this list's.
    [Fact]
    public async Task Leaves_an_existing_category_untouched()
    {
        await Given.NothingAsync();
        await Given.UserOwnedGroceriesCategoryAsync();

        await Seeder().SeedAsync(Ct);

        Assert.Equal("#000000", await ScalarAsync<string>(
            "SELECT [color] FROM [Categories] WHERE [name] = 'Groceries';"));

        // And no rule for it: the starting bucket ships with a *new* category, so
        // re-running can never resurrect a rule deleted on purpose.
        Assert.Equal(0, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM [RuleConditions] WHERE [value] = 'Groceries';
            """));
    }

    /// Uncategorised is listed in the catalogue but shipped by schema.sql, so the
    /// seeder must skip it rather than collide on the unique name — and it must
    /// never gain a bucket, or every un-ruled transaction silently becomes Wants.
    [Fact]
    public async Task Keeps_the_uncategorised_row_schema_ships_and_never_buckets_it()
    {
        await Given.NothingAsync();

        await Seeder().SeedAsync(Ct);

        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM [Categories] WHERE [name] = 'Uncategorised';"));
        Assert.Equal(0, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM [RuleConditions] WHERE [value] = 'Uncategorised';"));
    }

    /// A seeded rule lands after the rules already there. They are ordered and
    /// first match wins, so inserting at the front would silently outrank
    /// everything the user had written.
    [Fact]
    public async Task Seeds_its_rules_after_the_ones_already_there()
    {
        await Given.NothingAsync();
        await Given.UserOwnedBucketRuleAsync();

        await Seeder().SeedAsync(Ct);

        Assert.Equal(0, await ScalarAsync<int>(
            $"SELECT [position] FROM [Rules] WHERE [id] = '{Arrange.UserRuleId}';"));
        Assert.Equal(1, await ScalarAsync<int>(
            $"SELECT MIN([position]) FROM [Rules] WHERE [id] <> '{Arrange.UserRuleId}';"));
    }
}

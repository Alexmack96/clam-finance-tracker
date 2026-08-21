using Dapper;
using Microsoft.Data.SqlClient;

namespace Clam.Api.Tests;

/// A test about the test harness, and it earns its place.
///
/// Every other test in this assembly opens with a world from <see cref="Arrange"/>
/// and trusts that the database was empty first. Nothing else checks that, and
/// the way it fails is the worst kind: rows left standing make some later test
/// fail, the later test looks wrong, and the actual cause is a table nobody
/// cleared.
///
/// The reset used to be a hand-written list of DELETEs, so the cause was
/// usually "a table was added to db/schema.sql and not to that list". Respawn
/// derives the order from the schema's own foreign keys and cannot miss a table,
/// but this asserts the outcome rather than trusting the library — and it keeps
/// asserting it for whatever gets added next.
public class DatabaseResetTests(ClamApiFactory api) : ApiTest(api)
{
    /// Counts rows in every user table at once, from the partition metadata
    /// rather than a UNION of COUNT(*) per table. The point is to name no tables:
    /// a query listing them would need the same maintenance the DELETE list did.
    private const string RowsEverywhereSql = """
        SELECT  ISNULL(SUM(p.[rows]), 0)
        FROM    sys.tables t
        JOIN    sys.partitions p
                ON  p.[object_id] = t.[object_id]
                AND p.[index_id] IN (0, 1);
        """;

    private const string TablesWithRowsSql = """
        SELECT  t.[name]
        FROM    sys.tables t
        JOIN    sys.partitions p
                ON  p.[object_id] = t.[object_id]
                AND p.[index_id] IN (0, 1)
        GROUP BY t.[name]
        HAVING  SUM(p.[rows]) > 0
        ORDER BY t.[name];
        """;

    [Fact]
    public async Task Leaves_no_row_in_any_table()
    {
        // A world that populates a good spread of tables — categories,
        // transactions, statement files, staged bank rows — so the assertion has
        // something to clear rather than passing on an already-empty database.
        await Given.SeededAsync();
        await Given.StagedAmexRowAsync();

        await using var before = new SqlConnection(Api.ConnectionString);
        await before.OpenAsync(Ct);
        Assert.True(
            await before.ExecuteScalarAsync<long>(new CommandDefinition(RowsEverywhereSql, cancellationToken: Ct)) > 0,
            "the world built nothing, so this test would pass without proving anything");

        await Given.NothingAsync();

        await using var after = new SqlConnection(Api.ConnectionString);
        await after.OpenAsync(Ct);

        var survivors = (await after.QueryAsync<string>(
            new CommandDefinition(TablesWithRowsSql, cancellationToken: Ct))).ToList();

        Assert.True(
            survivors.Count == 0,
            $"NothingAsync left rows in: {string.Join(", ", survivors)}. "
            + "If one of these is new, the reset is not covering it.");
    }
}

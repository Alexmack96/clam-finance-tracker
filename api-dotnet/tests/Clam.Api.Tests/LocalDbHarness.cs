using Microsoft.Data.SqlClient;

namespace Clam.Api.Tests;

/// Creates and drops a throwaway database on the machine's LocalDB instance.
///
/// PremPoints uses Testcontainers.MsSql for this, which is the better answer on
/// a build agent — it pins the exact server version and needs nothing installed.
/// It needs Docker, and there is no Docker on this machine. LocalDB is already
/// here (it is what appsettings.Development.json points at), so the suite runs
/// with no prerequisites at all.
///
/// The trade is real and worth stating: LocalDB is not Azure SQL. It will not
/// catch a difference in an Azure-only feature. Everything the schema uses —
/// filtered indexes, CHECK constraints, DECIMAL(18,2), DATETIME2(3) — behaves
/// identically, which is why the trade is acceptable for now. Swapping this
/// class for a Testcontainers one is the only change needed if Docker arrives.
internal static class LocalDbHarness
{
    private static int _sweepDone;

    private const string MasterConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True;";

    internal static string ConnectionStringFor(string databaseName) =>
        $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;";

    /// Unique per fixture instance, so two fixtures — and therefore two test
    /// classes that mutate data — never share a database.
    internal static string NewDatabaseName() => $"ClamTest_{Guid.NewGuid():N}";

    internal static async Task CreateDatabaseAsync(string databaseName, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync(ct);

        // Not parameterisable — an identifier cannot be a parameter. The name is
        // generated above from a Guid, never from input.
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}];";
        await command.ExecuteNonQueryAsync(ct);
    }

    /// Applies db/schema.sql verbatim. The file is deliberately free of `GO`
    /// batch separators (see its header), which is what lets it go through
    /// ExecuteNonQuery as a single statement.
    internal static async Task ApplySchemaAsync(string databaseName, CancellationToken ct = default)
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "db", "schema.sql");

        if (!File.Exists(schemaPath))
        {
            throw new FileNotFoundException(
                $"schema.sql was not copied to the test output. Expected it at {schemaPath} — " +
                "check the <None Include=\"..\\..\\db\\schema.sql\" ... /> item in Clam.Api.Tests.csproj.",
                schemaPath);
        }

        var sql = await File.ReadAllTextAsync(schemaPath, ct);

        await using var connection = new SqlConnection(ConnectionStringFor(databaseName));
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    /// Drops every ClamTest_ database left behind by a previous run.
    ///
    /// Cleanup is done at the start rather than at the end because a fixture's
    /// teardown fires when the first test class using it finishes, not when the
    /// last one does — see the note on ClamApp.TearDownAsync. Databases created
    /// during *this* run are named after it starts, so the sweep cannot delete
    /// one that is in use.
    internal static async Task DropStaleDatabasesAsync(CancellationToken ct = default)
    {
        // Exactly once per test process. Every fixture calls this from
        // PreSetupAsync, and the second fixture to boot must not sweep away the
        // database the first one is actively using.
        if (Interlocked.Exchange(ref _sweepDone, 1) == 1)
        {
            return;
        }

        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync(ct);

        await using var list = connection.CreateCommand();
        list.CommandText = "SELECT [name] FROM sys.databases WHERE [name] LIKE 'ClamTest[_]%';";

        var stale = new List<string>();
        await using (var reader = await list.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                stale.Add(reader.GetString(0));
            }
        }

        foreach (var name in stale)
        {
            // Best effort: another `dotnet test` running concurrently may own one
            // of these, and failing the whole suite over cleanup would be worse
            // than leaving it behind.
            try
            {
                await DropDatabaseAsync(name, ct);
            }
            catch (SqlException)
            {
                // Ignored on purpose, per above.
            }
        }
    }

    internal static async Task DropDatabaseAsync(string databaseName, CancellationToken ct = default)
    {
        // The pool holds connections open to a database that is about to be
        // dropped; without this the DROP blocks and then fails.
        SqlConnection.ClearAllPools();

        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID('{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END
            """;
        await command.ExecuteNonQueryAsync(ct);
    }
}

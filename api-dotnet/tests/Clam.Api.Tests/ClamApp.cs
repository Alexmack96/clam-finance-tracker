using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Clam.Api.Tests;

/// Boots the real Program.cs against a throwaway LocalDB database.
///
/// Nothing is stubbed. The endpoints, the FastEndpoints serialiser config, the
/// exception handler, the validators and the SQL all run exactly as they do in
/// production — which is the point, because the thing most likely to break in
/// this service is a mismatch between hand-written SQL and the wire format, and
/// a mocked data layer cannot see either.
public class ClamApp : AppFixture<Program>
{
    private string _databaseName = null!;

    internal string ConnectionString { get; private set; } = null!;

    /// Runs before the host is built, so the database exists by the time
    /// Program.cs opens a connection to it.
    protected override async ValueTask PreSetupAsync()
    {
        await LocalDbHarness.DropStaleDatabasesAsync();

        _databaseName = LocalDbHarness.NewDatabaseName();
        ConnectionString = LocalDbHarness.ConnectionStringFor(_databaseName);

        await LocalDbHarness.CreateDatabaseAsync(_databaseName);
        await LocalDbHarness.ApplySchemaAsync(_databaseName);
    }

    protected override void ConfigureApp(IWebHostBuilder a)
    {
        ArgumentNullException.ThrowIfNull(a);

        a.UseEnvironment("Development");

        // AddInMemoryCollection, not UseSetting.
        //
        // UseSetting writes to *host* configuration, which WebApplicationBuilder
        // treats as a base layer — appsettings.Development.json is loaded after
        // it and would win. That file points at the real ClamFinanceTracker
        // database, so the suite would silently run against dev data and the
        // seed tests would delete it. This provider is appended last, so it wins.
        a.ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Clam"] = ConnectionString,

            // The seeder endpoint is only registered when this is on, and two
            // tests exercise it.
            ["Seed:Enabled"] = "true",

            // The limiter is global and partitions by IP; every test shares one.
            // The production budget of 120/min is enough today but would turn a
            // future longer suite into a flaky 429, so it is lifted here rather
            // than left as a trap.
            ["RateLimiting:PermitLimit"] = "100000",

            // Belt and braces: blank already means "register no JWT scheme", and
            // this guarantees a machine with WorkOS configured locally does not
            // change what the suite tests.
            ["WorkOS:ClientId"] = "",
        }));
    }

    // No SetupAsync seeding here: ApiTestBase re-seeds before every test, which
    // is the only level at which isolation actually holds. See the note there.

    /// Deliberately does NOT drop the database.
    ///
    /// A fixture instance is shared across every test class that uses it, but its
    /// teardown fires when the *first* of those classes finishes. Dropping here
    /// pulls the database out from under every later class, which fails in the
    /// most confusing way possible: each class passes on its own and the suite
    /// fails as a whole.
    ///
    /// Cleanup happens on the next run instead — PreSetupAsync sweeps stale
    /// ClamTest_ databases. LocalDB is a dev machine, and a few megabytes of
    /// leftover database until the next `dotnet test` is a much cheaper problem
    /// than an order-dependent suite.
    protected override ValueTask TearDownAsync() => ValueTask.CompletedTask;
}

/// A second fixture type gets its own SUT, and therefore its own database, but
/// only with [DisableWafCache] — by default FastEndpoints boots exactly one SUT
/// for the whole test project no matter how many AppFixture subclasses exist.
///
/// Used by the tests that POST /dev/seed, which deletes every row. Per-test
/// re-seeding in ApiTestBase already makes them safe, so this is belt and
/// braces: it also keeps a destructive endpoint's blast radius inside one
/// database, which is cheap insurance for the one endpoint that can drop data.
[DisableWafCache]
public sealed class ClamSeedApp : ClamApp;

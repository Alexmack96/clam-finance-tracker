using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

        // An environment variable, not UseSetting and not ConfigureAppConfiguration.
        //
        // This is the only mechanism that reliably wins. WebApplicationBuilder
        // builds its configuration inside Program.cs before WebApplicationFactory
        // gets a say, layering: host config -> appsettings.json ->
        // appsettings.Development.json -> user secrets -> environment variables.
        // UseSetting lands in the first layer and ConfigureAppConfiguration did
        // not reliably land in the last, so appsettings.Development.json won —
        // and it points at the real ClamFinanceTracker database.
        //
        // That is not a cosmetic bug: POST /dev/seed DELETEs every row, so the
        // suite would have wiped the dev database. VerifyTargetsOwnDatabaseAsync
        // below exists so this can never regress silently.
        //
        // Process-wide, but safe here: fixtures boot one at a time (test
        // parallelization is off) and each host captures configuration when it is
        // built, so a later fixture overwriting the variable cannot retarget an
        // already-running one.
        Environment.SetEnvironmentVariable("ConnectionStrings__Clam", ConnectionString);

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

    /// Fails the run if the booted app is not pointed at this fixture's throwaway
    /// database.
    ///
    /// The guard is here because the failure it catches is both silent and
    /// destructive. When the connection-string override loses to
    /// appsettings.Development.json, every read still returns *something* and the
    /// suite looks merely wrong — while POST /dev/seed quietly deletes the real
    /// dev database. Better to refuse to run.
    internal async Task VerifyTargetsOwnDatabaseAsync(CancellationToken ct)
    {
        var resolved = Services.GetRequiredService<IConfiguration>().GetConnectionString("Clam");

        if (!string.Equals(resolved, ConnectionString, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"""
                 The test host is not using this fixture's database.

                   fixture expected : {ConnectionString}
                   app resolved     : {resolved}

                 The connection-string override in ClamApp.ConfigureApp has stopped
                 winning over appsettings.Development.json. Refusing to run, because
                 POST /api/dev/seed would delete every row in the dev database.
                 """);
        }
    }

    /// Safe to drop here only because these are registered as *assembly* fixtures
    /// (see AssemblyInfo.cs), so this runs after the last test in the project.
    /// Under a class fixture it fired after the first test class and pulled the
    /// database out from under all the others.
    ///
    /// PreSetupAsync still sweeps stale databases, which covers the case this
    /// cannot: a run killed before teardown, which on a dev machine is most of
    /// them.
    protected override async ValueTask TearDownAsync()
        => await LocalDbHarness.DropDatabaseAsync(_databaseName);
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

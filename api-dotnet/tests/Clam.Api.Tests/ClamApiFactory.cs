using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Fx;
using Clam.Api.Infrastructure.Monzo;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Clam.Api.Tests;

/// Boots the real <c>Program.cs</c> in memory against a throwaway LocalDB
/// database.
///
/// Nothing about the API is stubbed. The endpoints, the serialiser
/// configuration, the validators, the exception handler and — most importantly —
/// the hand-written SQL all run exactly as they do in production. That last one
/// is the reason the database is real rather than faked: the failure this
/// service is most likely to have is a mismatch between a query and the wire
/// format, and neither a mocked connection nor a mocked repository can see it.
///
/// What *is* replaced is everything non-deterministic or outside the process:
/// the clock, the id generator, and the two outbound HTTP clients. See
/// <see cref="TestDoubles"/>.
public sealed class ClamApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// Frozen. Every "this month" filter, every expiry and every generated id
    /// derives from here, so a snapshot taken in March and one taken in August
    /// are the same file.
    public static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    private string _databaseName = null!;

    internal string ConnectionString { get; private set; } = null!;

    internal FakeTimeProvider Clock { get; } = new(Now);

    internal StubMonzoApiClient Monzo { get; } = new();

    /// Reset per test by <see cref="Arrange.NothingAsync"/>.
    internal SequentialIdGenerator Ids { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await LocalDbHarness.DropStaleDatabasesAsync();

        _databaseName = LocalDbHarness.NewDatabaseName();
        ConnectionString = LocalDbHarness.ConnectionStringFor(_databaseName);

        await LocalDbHarness.CreateDatabaseAsync(_databaseName);
        await LocalDbHarness.ApplySchemaAsync(_databaseName);

        // Force the host to build now, so a startup failure surfaces here rather
        // than inside the first test that happens to touch it.
        _ = Services;

        VerifyTargetsOwnDatabase();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        // An environment variable, not UseSetting and not ConfigureAppConfiguration.
        //
        // This is the only mechanism that reliably wins. WebApplicationBuilder
        // builds its configuration inside Program.cs before WebApplicationFactory
        // gets a say, layering: host config -> appsettings.json ->
        // appsettings.Development.json -> user secrets -> environment variables.
        // UseSetting lands in the first layer and ConfigureAppConfiguration did
        // not reliably land in the last, so appsettings.Development.json won.
        //
        // No committed connection string exists any more, so the failure this
        // guards against has changed shape rather than gone away: whatever the
        // developer has in user-secrets is now Azure. POST /api/dev/seed DELETEs
        // every row, so a suite that lost this override would empty the shared
        // dev database. VerifyTargetsOwnDatabase below exists so it cannot
        // regress silently.
        Environment.SetEnvironmentVariable("ConnectionStrings__Clam", ConnectionString);

        // Same mechanism, same reason. MonzoOptions is bound in Program.cs at
        // registration time from builder.Configuration, which is built before
        // ConfigureAppConfiguration below gets a say — so settings that only
        // appear there arrive too late and every Monzo endpoint answers 503
        // "not configured". None of these is a real secret; the API client
        // itself is stubbed.
        Environment.SetEnvironmentVariable("Monzo__ClientId", "client_test");
        Environment.SetEnvironmentVariable("Monzo__ClientSecret", "secret_test");
        Environment.SetEnvironmentVariable("Monzo__RedirectUri", "http://localhost/api/admin/monzo/callback");
        Environment.SetEnvironmentVariable("Monzo__ClientUrl", "http://localhost:5173");

        // Same mechanism, same reason again. The limiter is global, partitions by
        // IP, and every test in the assembly shares one SUT and one loopback
        // address — so the whole suite spends a single 120-request budget and
        // everything after the 120th request snapshots a 429 instead of its own
        // response. AddDefaultRateLimiting reads builder.Configuration at
        // registration time, so the in-memory override below arrives too late.
        Environment.SetEnvironmentVariable("RateLimiting__PermitLimit", "100000");

        // Same mechanism, and this one is about determinism rather than access.
        // The system category seeder is a BackgroundService: left on, it races
        // Respawn, and a test that arranged an empty world finds twelve
        // categories in it because the seeder caught up mid-run. It is driven
        // directly by SystemCategorySeederTests instead.
        Environment.SetEnvironmentVariable("SystemCategories__Seed", "false");

        // Same mechanism, third time, and this one decides whether the suite can
        // run at all. A blank WorkOS:ClientId registers no JWT scheme, which is
        // what leaves every endpoint anonymous — see AddWorkOsAuthentication.
        // appsettings.Development.json now carries a real client id, and the
        // in-memory override below loses to it, so without this every test gets
        // 401 and 205 of them fail at once.
        //
        // A space rather than "". Environment.SetEnvironmentVariable *deletes*
        // the variable when handed null or an empty string, which would leave
        // appsettings.Development.json winning again. WorkOsOptions tests with
        // IsNullOrWhiteSpace, so a space reads as "not configured".
        Environment.SetEnvironmentVariable("WorkOS__ClientId", " ");

        // And again for the seeder. appsettings.Development.json now sets this
        // false, because Development points at a shared Azure database and
        // POST /dev/seed opens by deleting every transaction and category. The
        // suite still needs the endpoint registered — it has tests for its
        // validator — and its own database is a throwaway created seconds ago,
        // so here it is safe and has to win.
        Environment.SetEnvironmentVariable("Seed__Enabled", "true");

        builder.ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Clam"] = ConnectionString,

            // Somewhere harmless for the statement store to resolve to. The Amex
            // upload tests do write real PDFs here, which is fine to leave
            // behind: a storage key is derived from the file's content hash, so
            // re-running the suite overwrites rather than accumulates.
            ["Statements:Directory"] = Path.Combine(Path.GetTempPath(), "clam-tests", "statements"),
        }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            // Sequential ids, so a snapshot shows `ctest-001` rather than a
            // freshly minted cuid nobody can approve.
            services.RemoveAll<IIdGenerator>();
            services.AddSingleton<IIdGenerator>(Ids);

            // The two outbound dependencies. A test that reaches the real Monzo
            // or Frankfurter is not a test, it is a network call.
            services.RemoveAll<IMonzoApiClient>();
            services.AddSingleton<IMonzoApiClient>(Monzo);

            services.RemoveAll<IFxRateService>();
            services.AddSingleton<IFxRateService, StubFxRateService>();
        });
    }

    /// Throws if the booted app is not pointed at this harness's throwaway
    /// database.
    ///
    /// The guard exists because the failure it catches is both silent and
    /// destructive: when the connection-string override loses to
    /// appsettings.Development.json every read still returns *something*, while
    /// POST /api/dev/seed quietly deletes the real dev database.
    internal void VerifyTargetsOwnDatabase()
    {
        var resolved = Services.GetRequiredService<IConfiguration>().GetConnectionString("Clam");

        if (!string.Equals(resolved, ConnectionString, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"""
                 The test host is not using this harness's database.

                   harness expected : {ConnectionString}
                   app resolved     : {resolved}

                 The connection-string override in ClamApiFactory has stopped winning
                 over appsettings.Development.json. Refusing to run, because
                 POST /api/dev/seed would delete every row in the dev database.
                 """);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await LocalDbHarness.DropDatabaseAsync(_databaseName);
        GC.SuppressFinalize(this);
    }
}

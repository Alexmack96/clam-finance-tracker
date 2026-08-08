namespace Clam.Api.Tests.Features;

/// Three health surfaces, and the differences between them are deliberate:
///
///   /api/health  — the FastEndpoints slice. Never touches the database, because
///                  Railway restarts a container whose healthcheck fails and an
///                  auto-paused Azure SQL would turn a cold start into a loop.
///   /alive       — Aspire's liveness probe. Same guarantee, "live"-tagged only.
///   /healthz     — the deep readiness report, one entry per dependency.
public class HealthTests(ClamApp app) : ApiTestBase<ClamApp>(app)
{
    [Fact]
    public async Task Api_health_returns_the_express_compatible_shape()
    {
        using var json = await GetJsonAsync("/api/health");

        json.RootElement.GetProperty("status").GetString().ShouldBe("ok");
    }

    [Fact]
    public async Task Alive_reports_only_the_self_check()
    {
        var response = await App.Client.GetAsync("/alive", Ct);

        response.IsSuccessStatusCode.ShouldBeTrue();
        // Plain text by design — the Aspire dashboard only reads the status code,
        // and a liveness probe that serialises anything can fail while the
        // process is fine.
        (await ReadTextAsync(response)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Healthz_reports_each_dependency_separately()
    {
        using var json = await GetJsonAsync("/healthz");

        json.RootElement.GetProperty("status").GetString().ShouldBe("Healthy");

        var entries = json.RootElement.GetProperty("entries");
        entries.TryGetProperty("self", out _).ShouldBeTrue();

        // The SQL check ran against the throwaway test database. If this is
        // missing, AddSqlServer(...) fell out of AddPersistence and /healthz has
        // quietly stopped reporting on the one dependency that matters.
        entries.TryGetProperty("sql", out var sql).ShouldBeTrue();
        sql.GetProperty("status").GetString().ShouldBe("Healthy");
    }
}

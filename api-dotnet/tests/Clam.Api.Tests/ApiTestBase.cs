using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FastEndpoints.Testing;

namespace Clam.Api.Tests;

/// The same tier-2 rule the API follows, applied to the tests: request plumbing
/// every test class repeats moves up one level, and nothing else does. Assertions
/// stay in the test that makes them.
///
/// It also puts the ambient cancellation token in one place. xUnit v3 wants every
/// awaited call to observe TestContext.Current.CancellationToken (xUnit1051), and
/// threading that through by hand at each call site is how it ends up forgotten.
public abstract class ApiTestBase<TApp>(TApp app) : TestBaseWithAssemblyFixture<TApp>
    where TApp : ClamApp
{
    protected TApp App { get; } = app;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// Re-seeds before every test, not once per class.
    ///
    /// A FastEndpoints AppFixture boots one SUT for the whole project and shares
    /// the instance across every test class using it, so per-class setup gives no
    /// isolation: one class's POST /dev/seed replaces the data another class is
    /// asserting against. Resetting per test makes every test independent of what
    /// ran before it, which is the only property that actually holds up.
    ///
    /// Affordable because the dataset is five categories and four rows against
    /// LocalDB. If it ever stops being affordable, the answer is a transaction
    /// rolled back per test, not a return to per-class setup.
    protected override async ValueTask SetupAsync()
    {
        await App.VerifyTargetsOwnDatabaseAsync(Ct);
        await TestDataSeeder.SeedAsync(App.ConnectionString, Ct);
    }

    protected async Task<JsonDocument> GetJsonAsync(
        string url,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await App.Client.GetAsync(url, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        // The body is in the assertion message on purpose. A bare
        // "expected OK but was InternalServerError" says nothing about which SQL
        // broke, and the ProblemDetails the API returns names it.
        response.StatusCode.ShouldBe(expected, $"GET {url} returned:{Environment.NewLine}{body}");

        return JsonDocument.Parse(body);
    }

    protected Task<HttpResponseMessage> PostAsync<TBody>(string url, TBody body)
        => App.Client.PostAsJsonAsync(url, body, Ct);

    protected static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
    }

    protected static async Task<string> ReadTextAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return await response.Content.ReadAsStringAsync(Ct);
    }
}

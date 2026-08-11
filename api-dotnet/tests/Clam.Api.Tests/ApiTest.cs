using System.Net.Http.Json;

namespace Clam.Api.Tests;

/// The base every test class derives from. It exists to make the three lines of
/// a test the *only* lines of a test: arrange, act, assert.
///
/// The act line is a plain HttpClient call, which is the point — these are
/// tests of the HTTP boundary, so nothing between the request and the response
/// is faked, and the assertion is a snapshot of what actually came back.
public abstract class ApiTest(ClamApiFactory api)
{
    protected ClamApiFactory Api { get; } = api;

    /// Named `Given` so an arrange line reads as one: `await Given.SeededAsync()`.
    protected Arrange Given { get; } = new(api.ConnectionString, api.Ids.Reset);

    protected HttpClient Client { get; } = api.CreateClient();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected Task<HttpResponseMessage> Get(string url) => Client.GetAsync(url, Ct);

    protected Task<HttpResponseMessage> Post(string url, object? body = null)
        => Client.PostAsJsonAsync(url, body ?? new { }, Ct);

    protected Task<HttpResponseMessage> Patch(string url, object body)
        => Client.PatchAsJsonAsync(url, body, Ct);

    protected Task<HttpResponseMessage> Put(string url, object body)
        => Client.PutAsJsonAsync(url, body, Ct);

    protected Task<HttpResponseMessage> Delete(string url) => Client.DeleteAsync(url, Ct);
}

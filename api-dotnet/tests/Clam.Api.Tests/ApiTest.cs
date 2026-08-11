using System.Net.Http.Json;
using System.Text;

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

    /// A body written as a literal JSON string rather than serialised from an
    /// anonymous object.
    ///
    /// The typed helpers above can only send JSON that C#'s type system can
    /// produce, which is a strictly smaller set than what reaches a public API:
    /// a string where a number belongs, a null on a non-nullable, a malformed
    /// document. Those fail in the *binder*, before any validator runs, and this
    /// is the only way to write one.
    protected Task<HttpResponseMessage> PostRaw(string url, string json)
        => Client.PostAsync(url, JsonBody(json), Ct);

    protected Task<HttpResponseMessage> PatchRaw(string url, string json)
        => Client.PatchAsync(url, JsonBody(json), Ct);

    private static StringContent JsonBody(string json)
        => new(json, Encoding.UTF8, "application/json");
}

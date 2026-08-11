using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace Clam.Api.Infrastructure.Monzo;

public sealed record MonzoTokens(string AccessToken, string RefreshToken, DateTime ExpiresAt);

public sealed record MonzoAccount(string Id, string Type, bool Closed);

/// A transaction as the Monzo API returns it. Snake case throughout, because
/// that is the API's spelling and renaming it here would need a second name for
/// every field for no gain.
public sealed class MonzoTransaction
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("created")] public DateTime Created { get; set; }
    [JsonPropertyName("settled")] public string? Settled { get; set; }
    [JsonPropertyName("amount")] public int Amount { get; set; }
    [JsonPropertyName("currency")] public string Currency { get; set; } = "";
    [JsonPropertyName("local_amount")] public int LocalAmount { get; set; }
    [JsonPropertyName("local_currency")] public string LocalCurrency { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("notes")] public string? Notes { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("decline_reason")] public string? DeclineReason { get; set; }
    [JsonPropertyName("scheme")] public string? Scheme { get; set; }
    [JsonPropertyName("include_in_spending")] public bool IncludeInSpending { get; set; }
    [JsonPropertyName("account_id")] public string AccountId { get; set; } = "";
    [JsonPropertyName("merchant")] public MonzoMerchant? Merchant { get; set; }

    /// A pending transaction has no settled timestamp. Those are excluded
    /// everywhere: an authorisation can vanish, and importing one produces a
    /// transaction with no counterpart on the statement.
    public bool IsSettled => !string.IsNullOrEmpty(Settled) && string.IsNullOrEmpty(DeclineReason);
}

public sealed class MonzoMerchant
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("emoji")] public string? Emoji { get; set; }
    [JsonPropertyName("address")] public MonzoMerchantAddress? Address { get; set; }
}

public sealed class MonzoMerchantAddress
{
    [JsonPropertyName("short_formatted")] public string? ShortFormatted { get; set; }
}

/// Raised for a non-2xx from Monzo, so a slice can answer 502 with the body
/// rather than a 500 with a stack trace.
public sealed class MonzoApiException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public interface IMonzoApiClient
{
    Task<MonzoTokens> ExchangeCodeAsync(string code, CancellationToken ct = default);
    Task<MonzoTokens> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task<IReadOnlyList<MonzoAccount>> GetAccountsAsync(string accessToken, CancellationToken ct = default);

    /// Every transaction from <paramref name="since"/> onward, following Monzo's
    /// cursor pagination. `since` may be an ISO timestamp or a transaction id.
    Task<List<MonzoTransaction>> GetTransactionsAsync(
        string accountId, string accessToken, string since, CancellationToken ct = default);
}

public sealed class MonzoApiClient(HttpClient http, MonzoOptions options, TimeProvider clock) : IMonzoApiClient
{
    private const int PageSize = 100;

    /// Monzo's cursor never says "that was the last page", so the loop stops on
    /// a short page. The cap is a backstop against a cursor that stops advancing.
    private const int MaxPages = 50;

    public Task<MonzoTokens> ExchangeCodeAsync(string code, CancellationToken ct = default) =>
        PostTokenAsync(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = options.ClientId!,
            ["client_secret"] = options.ClientSecret!,
            ["redirect_uri"] = options.RedirectUri!,
            ["code"] = code,
        }, ct);

    public Task<MonzoTokens> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
        PostTokenAsync(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = options.ClientId!,
            ["client_secret"] = options.ClientSecret!,
            ["refresh_token"] = refreshToken,
        }, ct);

    public async Task<IReadOnlyList<MonzoAccount>> GetAccountsAsync(string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "accounts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        var payload = await response.Content.ReadFromJsonAsync<AccountsPayload>(ct);
        return payload?.Accounts ?? [];
    }

    public async Task<List<MonzoTransaction>> GetTransactionsAsync(
        string accountId, string accessToken, string since, CancellationToken ct = default)
    {
        var all = new List<MonzoTransaction>();
        var cursor = since;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"transactions?account_id={Uri.EscapeDataString(accountId)}"
                + $"&since={Uri.EscapeDataString(cursor)}&limit={PageSize}&expand[]=merchant";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await http.SendAsync(request, ct);
            await EnsureSuccessAsync(response, ct);

            var payload = await response.Content.ReadFromJsonAsync<TransactionsPayload>(ct);
            var batch = payload?.Transactions ?? [];

            all.AddRange(batch);
            if (batch.Count < PageSize) break;

            cursor = batch[^1].Id;
        }

        return all;
    }

    private async Task<MonzoTokens> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync("oauth2/token", content, ct);
        await EnsureSuccessAsync(response, ct);

        var payload = await response.Content.ReadFromJsonAsync<TokenPayload>(ct)
            ?? throw new MonzoApiException("Monzo returned an empty token response", 502);

        // Monzo returns a lifetime, not an instant, so the expiry is derived from
        // the injected clock rather than read off the wall.
        return new MonzoTokens(
            payload.AccessToken,
            payload.RefreshToken,
            clock.GetUtcNow().UtcDateTime.AddSeconds(payload.ExpiresIn));
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        // The body is included because Monzo's error messages are the useful
        // part — "insufficient_permissions" versus "expired token" is the
        // difference between reconnecting and waiting.
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new MonzoApiException(body, (int)response.StatusCode);
    }

    private sealed record TokenPayload(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record AccountsPayload(
        [property: JsonPropertyName("accounts")] List<MonzoAccount> Accounts);

    private sealed record TransactionsPayload(
        [property: JsonPropertyName("transactions")] List<MonzoTransaction> Transactions);
}

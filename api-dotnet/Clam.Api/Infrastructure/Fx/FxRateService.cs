using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Clam.Api.Infrastructure.Fx;

/// <param name="Amount">The converted amount, rounded to 2dp.</param>
/// <param name="Rate">The rate actually used.</param>
/// <param name="Fallback">null on the happy path; otherwise which fallback was used.</param>
public readonly record struct FxConversion(decimal Amount, decimal Rate, string? Fallback);

public interface IFxRateService
{
    /// Converts without ever throwing. A Frankfurter outage must not block an
    /// import — losing the row is worse than booking it at a slightly wrong rate,
    /// and every fallback is logged with the row's id so it can be corrected.
    Task<FxConversion> ConvertWithFallbackAsync(
        decimal amount, string from, string to, DateTime date, string contextId, CancellationToken ct = default);
}

/// Historical FX via Frankfurter — free, no API key, ECB data. Frankfurter
/// already falls back to the nearest previous weekday for a weekend or holiday
/// date, so that case needs no handling here.
public sealed class FrankfurterFxRateService(HttpClient http, ILogger<FrankfurterFxRateService> logger)
    : IFxRateService
{
    /// Last-resort rates for when both the historical and the latest lookup
    /// fail. Deliberately approximate: they exist to prevent data loss, not to
    /// be accurate.
    private static readonly Dictionary<string, decimal> HardcodedFallbacks = new(StringComparer.Ordinal)
    {
        ["USD:GBP"] = 0.79m,
    };

    // Process-lifetime, unbounded by design: a /process run converts thousands of
    // rows across a few hundred distinct dates, and the pairs are a closed set.
    private readonly ConcurrentDictionary<string, decimal> _cache = new(StringComparer.Ordinal);

    public async Task<FxConversion> ConvertWithFallbackAsync(
        decimal amount, string from, string to, DateTime date, string contextId, CancellationToken ct = default)
    {
        if (string.Equals(from, to, StringComparison.Ordinal))
            return new FxConversion(amount, 1m, null);

        var iso = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        try
        {
            var rate = await GetRateAsync(from, to, iso, ct);
            return new FxConversion(Round2(amount * rate), rate, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            logger.LogWarning(ex, "FX historical rate failed for {ContextId} ({From}->{To} on {Date})",
                contextId, from, to, iso);
        }

        try
        {
            var rate = await GetRateAsync(from, to, "latest", ct);
            logger.LogWarning("FX using latest rate {Rate} as fallback for {ContextId} ({From}->{To}, requested {Date})",
                rate, contextId, from, to, iso);
            return new FxConversion(Round2(amount * rate), rate, "latest");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            logger.LogError(ex, "FX latest rate also failed for {ContextId} ({From}->{To})", contextId, from, to);
        }

        var hardcoded = HardcodedFallbacks.GetValueOrDefault($"{from}:{to}", 1m);
        logger.LogError("FX using hardcoded fallback {Rate} for {ContextId} ({From}->{To} on {Date}) — fix manually",
            hardcoded, contextId, from, to, iso);
        return new FxConversion(Round2(amount * hardcoded), hardcoded, "hardcoded");
    }

    /// <param name="datePath">An ISO date, or the literal "latest".</param>
    private async Task<decimal> GetRateAsync(string from, string to, string datePath, CancellationToken ct)
    {
        var key = $"{from}:{to}:{datePath}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var response = await http.GetFromJsonAsync<FrankfurterResponse>(
            $"v1/{datePath}?base={from}&symbols={to}", ct);

        if (response?.Rates is null || !response.Rates.TryGetValue(to, out var rate))
            throw new InvalidOperationException($"Missing {to} rate in Frankfurter response for {datePath}");

        _cache[key] = rate;
        return rate;
    }

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record FrankfurterResponse(
        [property: JsonPropertyName("base")] string? Base,
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("rates")] Dictionary<string, decimal>? Rates);
}

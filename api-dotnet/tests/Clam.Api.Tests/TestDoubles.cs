using System.Globalization;
using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Fx;
using Clam.Api.Infrastructure.Monzo;

namespace Clam.Api.Tests;

/// Ids that a human can approve in a snapshot.
///
/// The real generator is a cuid derived from the clock and a random block, which
/// would make every snapshot differ from the last. Sequential ids keep the
/// property that matters here — uniqueness within a run — and drop the one that
/// does not.
internal sealed class SequentialIdGenerator : IIdGenerator
{
    private int _next;

    public string NewId() =>
        "cgen" + Interlocked.Increment(ref _next).ToString("D20", CultureInfo.InvariantCulture);

    /// Called from <see cref="Arrange.NothingAsync"/>, so the counter restarts
    /// with the database it numbers.
    ///
    /// Without this the generator is a singleton on an assembly-scoped host, so
    /// the id a test sees depends on how many ids every test before it happened
    /// to consume. Snapshots then pass on the run that approved them and fail
    /// the moment the suite is filtered, reordered or added to.
    internal void Reset() => Interlocked.Exchange(ref _next, 0);
}

/// A fixed rate, so a USD import converts to a number the snapshot can state.
///
/// The real service calls Frankfurter and falls back twice; that behaviour is
/// worth its own unit tests, but no integration test should depend on a third
/// party being up.
internal sealed class StubFxRateService : IFxRateService
{
    internal const decimal UsdToGbp = 0.80m;

    public Task<FxConversion> ConvertWithFallbackAsync(
        decimal amount, string from, string to, DateTime date, string contextId, CancellationToken ct = default)
    {
        if (string.Equals(from, to, StringComparison.Ordinal))
            return Task.FromResult(new FxConversion(amount, 1m, null));

        return Task.FromResult(new FxConversion(
            Math.Round(amount * UsdToGbp, 2, MidpointRounding.AwayFromZero), UsdToGbp, null));
    }
}

/// A Monzo API that answers from whatever a test put in it.
///
/// Public fields rather than a mocking framework: the whole surface is four
/// methods, and a test that says `Monzo.Transactions = [...]` reads better than
/// three lines of setup expressions.
internal sealed class StubMonzoApiClient : IMonzoApiClient
{
    internal List<MonzoAccount> Accounts { get; set; } =
        [new MonzoAccount("acc_retail", "uk_retail", false)];

    internal List<MonzoTransaction> Transactions { get; set; } = [];

    internal MonzoTokens Tokens { get; set; } =
        new("access_test", "refresh_test", ClamApiFactory.Now.UtcDateTime.AddHours(6));

    /// Set to make the next call fail, which is how the 502 and 401 paths are
    /// reached without a real outage.
    internal MonzoApiException? Failure { get; set; }

    public Task<MonzoTokens> ExchangeCodeAsync(string code, CancellationToken ct = default) =>
        Failure is not null ? Task.FromException<MonzoTokens>(Failure) : Task.FromResult(Tokens);

    public Task<MonzoTokens> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
        Failure is not null ? Task.FromException<MonzoTokens>(Failure) : Task.FromResult(Tokens);

    public Task<IReadOnlyList<MonzoAccount>> GetAccountsAsync(string accessToken, CancellationToken ct = default) =>
        Failure is not null
            ? Task.FromException<IReadOnlyList<MonzoAccount>>(Failure)
            : Task.FromResult<IReadOnlyList<MonzoAccount>>(Accounts);

    public Task<List<MonzoTransaction>> GetTransactionsAsync(
        string accountId, string accessToken, string since, CancellationToken ct = default) =>
        Failure is not null
            ? Task.FromException<List<MonzoTransaction>>(Failure)
            : Task.FromResult(Transactions.Where(t => t.AccountId == accountId).ToList());
}

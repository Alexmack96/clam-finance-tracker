using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Monzo;
using Clam.Api.Infrastructure.Results;
using Dapper;

namespace Clam.Api.Features.Monzo.SyncMonzo;

public sealed class SyncMonzoCommand(
    IDbConnectionFactory factory,
    IIdGenerator ids,
    IMonzoApiClient api,
    MonzoConnectionResolver resolver,
    MonzoReconciler reconciler)
{
    /// Where the very first sync starts from when an account has no stored
    /// transactions. Monzo will not serve more than 90 days once the token is
    /// past its authentication window anyway, so this is a floor, not a promise.
    private const string InitialCursor = "2025-12-31T23:59:59.000Z";

    private const string LatestSql = """
        SELECT TOP 1 [monzoId]
        FROM   [MonzoApiTransactions]
        WHERE  [accountId] = @AccountId
        ORDER BY [created] DESC;
        """;

    public async Task<Result<SyncMonzoResponse>> ExecuteAsync(CancellationToken ct)
    {
        var resolved = await resolver.ResolveAsync(ct);
        if (!resolved.IsSuccess) return resolved.PropagateFailure<MonzoConnectionResolver.Connection, SyncMonzoResponse>();

        var (credential, accounts) = (resolved.Value.Credential, resolved.Value.Accounts);

        var imported = 0;
        var duplicates = 0;

        using var connection = await factory.OpenAsync(ct);

        foreach (var account in accounts)
        {
            // `since` must be a timestamp or a transaction id belonging to *this*
            // account, so the cursor is tracked per account rather than globally.
            var latest = await connection.QuerySingleOrDefaultAsync<string>(
                new CommandDefinition(LatestSql, new { AccountId = account.Id }, cancellationToken: ct));

            List<MonzoTransaction> raw;
            try
            {
                raw = await api.GetTransactionsAsync(account.Id, credential.AccessToken, latest ?? InitialCursor, ct);
            }
            catch (MonzoApiException ex)
            {
                return Result<SyncMonzoResponse>.Error(
                    $"Monzo /transactions failed for {account.Type}: {ex.Message}");
            }

            var settled = raw.Where(t => t.IsSettled).ToList();
            if (settled.Count == 0) continue;

            // The insert is idempotent, so the difference between what was sent
            // and what landed is the duplicate count — no separate read needed.
            var inserted = await MonzoStagingWriter.StageAsync(connection, ids, settled, account.Id, ct);
            imported += inserted;
            duplicates += settled.Count - inserted;
        }

        // Safety net against the full 90-day window: backfills anything the
        // incremental pass above missed, and records the run either way.
        var run = await reconciler.ReconcileAsync(accounts, credential.AccessToken, "sync", ct);

        return Result<SyncMonzoResponse>.Success(new SyncMonzoResponse
        {
            Imported = imported + run.TotalBackfilled,
            Duplicates = duplicates,
            Reconciled = new SyncReconciliation
            {
                Window = run.Window,
                Missing = run.TotalMissing,
                Backfilled = run.TotalBackfilled,
            },
        });
    }
}

using System.Text.Json;
using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Monzo;
using Dapper;

namespace Clam.Api.Features.Monzo;

/// A gap the rec found: present in the Monzo API, absent from staging.
public sealed class RecMissingTransaction
{
    public string Id { get; set; } = "";
    public DateTime Created { get; set; }
    public int AmountPence { get; set; }
    public string Description { get; set; } = "";

    /// Whether the backfill actually managed to stage it.
    public bool Recovered { get; set; }
}

/// Per-account outcome of one rec pass, persisted as JSON on the run.
public sealed class RecAccountResult
{
    public string AccountId { get; set; } = "";
    public string AccountType { get; set; } = "";
    public int ApiSettledCount { get; set; }
    public int MissingCount { get; set; }
    public int BackfilledCount { get; set; }
    public IReadOnlyList<RecMissingTransaction> Missing { get; set; } = [];
    public string? Error { get; set; }
}

public sealed class MonzoRecRun
{
    public string Id { get; set; } = "";
    public DateTime RanAt { get; set; }
    public string Window { get; set; } = "";

    /// "sync" or "manual" — whether this ran off the back of a sync or was asked
    /// for from the admin page.
    public string Trigger { get; set; } = "";

    public int TotalMissing { get; set; }
    public int TotalBackfilled { get; set; }
    public IReadOnlyList<RecAccountResult> Results { get; set; } = [];
}

/// Independently pulls the full last-90-day window — the most the Monzo API
/// serves once a token is more than five minutes past authentication — finds any
/// settled transaction present in the API but absent from staging, and backfills
/// it.
///
/// This exists because the incremental sync only fetches *forward* from each
/// account's newest stored transaction, so anything an earlier sync missed would
/// never be re-fetched. Backfilled rows are staged exactly as the sync would
/// stage them, so they flow through the normal process step.
public sealed class MonzoReconciler(
    IDbConnectionFactory factory,
    IIdGenerator ids,
    IMonzoApiClient api,
    TimeProvider clock,
    ILogger<MonzoReconciler> logger)
{
    private const int WindowDays = 89;
    internal const string Window = "90d";

    private const string PresentIdsSql = "SELECT [monzoId] FROM [MonzoApiTransactions] WHERE [monzoId] IN @Ids;";

    private const string InsertRunSql = """
        INSERT INTO [MonzoRecRuns] ([id], [window], [trigger], [totalMissing], [totalBackfilled], [results])
        OUTPUT INSERTED.[id], INSERTED.[ranAt], INSERTED.[window], INSERTED.[trigger],
               INSERTED.[totalMissing], INSERTED.[totalBackfilled]
        VALUES (@Id, @Window, @Trigger, @TotalMissing, @TotalBackfilled, @Results);
        """;

    public async Task<MonzoRecRun> ReconcileAsync(
        IReadOnlyList<MonzoAccount> accounts,
        string accessToken,
        string trigger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var windowStart = clock.GetUtcNow().UtcDateTime.AddDays(-WindowDays);
        var results = new List<RecAccountResult>();
        var totalMissing = 0;
        var totalBackfilled = 0;

        using var connection = await factory.OpenAsync(ct);

        foreach (var account in accounts)
        {
            List<MonzoTransaction> apiTransactions;
            try
            {
                apiTransactions = await api.GetTransactionsAsync(
                    account.Id, accessToken, windowStart.ToString("O"), ct);
            }
            catch (MonzoApiException ex)
            {
                // Best-effort by design: one account failing must not lose the
                // rec for the others, and the failure is recorded in the run.
                logger.LogWarning("Monzo 90-day rec fetch failed for {AccountType} ({AccountId}): {Message}",
                    account.Type, account.Id, ex.Message);

                results.Add(new RecAccountResult
                {
                    AccountId = account.Id,
                    AccountType = account.Type,
                    Error = ex.Message,
                });
                continue;
            }

            var settled = apiTransactions.Where(t => t.IsSettled).ToList();
            var present = settled.Count == 0
                ? []
                : (await connection.QueryAsync<string>(new CommandDefinition(
                    PresentIdsSql, new { Ids = settled.Select(t => t.Id).ToArray() }, cancellationToken: ct)))
                    .ToHashSet(StringComparer.Ordinal);

            var missingTransactions = settled.Where(t => !present.Contains(t.Id)).ToList();

            var missing = new List<RecMissingTransaction>();
            var backfilled = 0;
            string? backfillError = null;

            foreach (var tx in missingTransactions)
            {
                var recovered = true;
                try
                {
                    await MonzoStagingWriter.StageAsync(connection, ids, [tx], account.Id, ct);
                    backfilled++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One row at a time, so a single bad row cannot sink the
                    // whole backfill.
                    recovered = false;
                    backfillError ??= ex.Message;
                }

                missing.Add(new RecMissingTransaction
                {
                    Id = tx.Id,
                    Created = tx.Created,
                    AmountPence = tx.Amount,
                    Description = tx.Merchant?.Name ?? tx.Description,
                    Recovered = recovered,
                });
            }

            totalMissing += missing.Count;
            totalBackfilled += backfilled;

            results.Add(new RecAccountResult
            {
                AccountId = account.Id,
                AccountType = account.Type,
                ApiSettledCount = settled.Count,
                MissingCount = missing.Count,
                BackfilledCount = backfilled,
                Missing = missing,
                Error = backfillError,
            });

            if (missing.Count > 0)
            {
                // A recovered gap is still a gap: it means the incremental sync
                // dropped something, which is worth knowing even once it is fixed.
                var recoveredAll = backfilled == missingTransactions.Count;
                logger.Log(
                    recoveredAll ? LogLevel.Warning : LogLevel.Error,
                    "Monzo 90-day rec for {AccountType} ({AccountId}): {Missing} missing, {Backfilled} backfilled",
                    account.Type, account.Id, missing.Count, backfilled);
            }
        }

        var json = JsonSerializer.Serialize(results, RecJson.Options);

        var run = await connection.QuerySingleAsync<MonzoRecRun>(new CommandDefinition(InsertRunSql, new
        {
            Id = ids.NewId(),
            Window,
            Trigger = trigger,
            TotalMissing = totalMissing,
            TotalBackfilled = totalBackfilled,
            Results = json,
        }, cancellationToken: ct));

        run.Results = results;
        return run;
    }
}

/// The `results` column is a JSON string in the database and a real array on the
/// wire, so both directions go through these options rather than the ambient
/// defaults — which are the endpoint serialiser's, not this column's.
internal static class RecJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

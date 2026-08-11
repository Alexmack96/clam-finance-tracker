using Clam.Api.Infrastructure.Data;
using Clam.Api.Infrastructure.Fx;
using Dapper;

namespace Clam.Api.Features.Import.BackfillUsdGbp;

public sealed class BackfillUsdGbpCommand(IDbConnectionFactory factory, IFxRateService fx)
{
    private const int MaxReportedErrors = 20;

    /// `originalAmount IS NULL` is the whole of the idempotency: a converted row
    /// records what it was converted from, so it can never be a candidate twice.
    private const string CandidatesSql = """
        SELECT  [id], [amount], [date], [externalId]
        FROM    [Transactions]
        WHERE   [originalAmount] IS NULL
          AND   ([externalId] LIKE 'sofi:%' OR [externalId] LIKE 'chase:%');
        """;

    private const string UpdateSql = """
        UPDATE  [Transactions]
        SET     [amount] = @Amount, [originalAmount] = @OriginalAmount, [originalCurrency] = 'USD'
        WHERE   [id] = @Id;
        """;

    public async Task<BackfillUsdGbpResponse> ExecuteAsync(BackfillUsdGbpRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);
        var rows = (await connection.QueryAsync<CandidateRow>(
            new CommandDefinition(CandidatesSql, cancellationToken: ct))).AsList();

        var converted = 0;
        var errored = 0;
        var errors = new List<string>();

        foreach (var row in rows)
        {
            var contextId = row.ExternalId ?? row.Id;
            try
            {
                var result = await fx.ConvertWithFallbackAsync(row.Amount, "USD", "GBP", row.Date, contextId, ct);

                if (!request.DryRun)
                {
                    await connection.ExecuteAsync(new CommandDefinition(
                        UpdateSql,
                        new { row.Id, result.Amount, OriginalAmount = row.Amount },
                        cancellationToken: ct));
                }

                converted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One row that cannot be converted must not abandon the rest —
                // this runs over rows that have been wrong for months.
                errored++;
                if (errors.Count < MaxReportedErrors) errors.Add($"{contextId}: {ex.Message}");
            }
        }

        return new BackfillUsdGbpResponse
        {
            Candidates = rows.Count,
            Converted = converted,
            Errored = errored,
            Errors = errors,
            DryRun = request.DryRun,
        };
    }

    private sealed class CandidateRow
    {
        public string Id { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public string? ExternalId { get; set; }
    }
}

using System.Text.Json;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Monzo.GetMonzoRecRuns;

public sealed class GetMonzoRecRunsQuery(IDbConnectionFactory factory)
{
    /// The most recent 20. A rec runs after every sync, so the full history is
    /// unbounded and nobody scrolls past the last few days of it.
    private const string Sql = """
        SELECT TOP 20 [id], [ranAt], [window], [trigger], [totalMissing], [totalBackfilled], [results]
        FROM   [MonzoRecRuns]
        ORDER BY [ranAt] DESC;
        """;

    public async Task<IReadOnlyList<MonzoRecRun>> ExecuteAsync(CancellationToken ct)
    {
        using var connection = await factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<RecRunRow>(new CommandDefinition(Sql, cancellationToken: ct));

        return [.. rows.Select(r => new MonzoRecRun
        {
            Id = r.Id,
            RanAt = r.RanAt,
            Window = r.Window,
            Trigger = r.Trigger,
            TotalMissing = r.TotalMissing,
            TotalBackfilled = r.TotalBackfilled,

            // Stored as a JSON string and sent as a real array, which is what the
            // Express route's `JSON.parse(run.results)` does.
            Results = JsonSerializer.Deserialize<List<RecAccountResult>>(r.Results, RecJson.Options) ?? [],
        })];
    }

    private sealed class RecRunRow
    {
        public string Id { get; set; } = "";
        public DateTime RanAt { get; set; }
        public string Window { get; set; } = "";
        public string Trigger { get; set; } = "";
        public int TotalMissing { get; set; }
        public int TotalBackfilled { get; set; }
        public string Results { get; set; } = "[]";
    }
}

using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Investments.DeleteInvestmentSnapshot;

public sealed class DeleteInvestmentSnapshotCommand(IDbConnectionFactory factory)
{
    private const string Sql = "DELETE FROM [InvestmentSnapshots] WHERE [id] = @Id;";

    public async Task<Result> ExecuteAsync(DeleteInvestmentSnapshotRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(Sql, new { request.Id }, cancellationToken: ct));

        return affected == 0 ? Result.NotFound("Snapshot not found") : Result.NoContent();
    }
}

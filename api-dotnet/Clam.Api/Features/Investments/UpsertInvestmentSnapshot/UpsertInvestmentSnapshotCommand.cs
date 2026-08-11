using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Clam.Api.Features.Investments.UpsertInvestmentSnapshot;

public sealed class UpsertInvestmentSnapshotCommand(IDbConnectionFactory factory, IIdGenerator ids)
{
    private const int ForeignKeyViolation = 547;

    private const string Sql = """
        MERGE   [InvestmentSnapshots] WITH (HOLDLOCK) AS target
        USING   (SELECT @AccountId AS [accountId], @Date AS [date]) AS source
        ON      target.[accountId] = source.[accountId] AND target.[date] = source.[date]
        WHEN MATCHED THEN
            UPDATE SET [value] = @Value, [updatedAt] = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN
            INSERT ([id], [accountId], [date], [value])
            VALUES (@Id, @AccountId, @Date, @Value)
        OUTPUT  INSERTED.[id], INSERTED.[accountId], INSERTED.[date],
                INSERTED.[value], INSERTED.[createdAt], INSERTED.[updatedAt];
        """;

    public async Task<Result<InvestmentSnapshotRecord>> ExecuteAsync(
        UpsertInvestmentSnapshotRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parameters = new { Id = ids.NewId(), request.AccountId, request.Date, request.Value };

        using var connection = await factory.OpenAsync(ct);

        try
        {
            var snapshot = await connection.QuerySingleAsync<InvestmentSnapshotRecord>(
                new CommandDefinition(Sql, parameters, cancellationToken: ct));

            return Result<InvestmentSnapshotRecord>.Success(snapshot);
        }
        catch (SqlException ex) when (ex.Number == ForeignKeyViolation)
        {
            return Result<InvestmentSnapshotRecord>.NotFound("Investment account not found");
        }
    }
}

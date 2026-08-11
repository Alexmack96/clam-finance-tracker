using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Investments.DeleteInvestmentSnapshotsByDate;

public sealed class DeleteInvestmentSnapshotsByDateCommand(IDbConnectionFactory factory)
{
    private const string Sql = """
        DELETE  s
        FROM    [InvestmentSnapshots] s
        JOIN    [InvestmentAccounts] a ON a.[id] = s.[accountId]
        WHERE   s.[date] = @Date AND a.[owner] = @Owner;
        """;

    public async Task<Result> ExecuteAsync(DeleteInvestmentSnapshotsByDateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parameters = new { request.Date, Owner = InvestmentOwner.Parse(request.Owner).ToString() };

        using var connection = await factory.OpenAsync(ct);

        // No 404 on zero rows: deleting a date that holds nothing is the state
        // the caller asked for, and the grid has already removed the column.
        await connection.ExecuteAsync(new CommandDefinition(Sql, parameters, cancellationToken: ct));
        return Result.NoContent();
    }
}

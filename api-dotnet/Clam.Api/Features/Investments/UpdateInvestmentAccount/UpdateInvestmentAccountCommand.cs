using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Investments.UpdateInvestmentAccount;

public sealed class UpdateInvestmentAccountCommand(IDbConnectionFactory factory)
{
    /// `rate` is nullable in the database, so COALESCE cannot express clearing
    /// it — the same limitation the Express route has, kept rather than fixed so
    /// the two services agree.
    private const string Sql = $"""
        UPDATE  [InvestmentAccounts]
        SET     [name]      = COALESCE(@Name, [name]),
                [category]  = COALESCE(@Category, [category]),
                [rate]      = COALESCE(@Rate, [rate]),
                [sortOrder] = COALESCE(@SortOrder, [sortOrder]),
                [updatedAt] = SYSUTCDATETIME()
        OUTPUT  {InvestmentSql.AccountInserted}
        WHERE   [id] = @Id;
        """;

    public async Task<Result<InvestmentAccountRecord>> ExecuteAsync(
        UpdateInvestmentAccountRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);
        var updated = await connection.QuerySingleOrDefaultAsync<InvestmentAccountRecord>(
            new CommandDefinition(Sql, request, cancellationToken: ct));

        return updated is null
            ? Result<InvestmentAccountRecord>.NotFound("Investment account not found")
            : Result<InvestmentAccountRecord>.Success(updated);
    }
}

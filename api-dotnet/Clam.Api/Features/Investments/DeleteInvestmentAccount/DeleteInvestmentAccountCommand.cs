using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Investments.DeleteInvestmentAccount;

public sealed class DeleteInvestmentAccountCommand(IDbConnectionFactory factory)
{
    /// Snapshots cascade on the FK. That is correct here and not the compromise
    /// it is for statement files: a snapshot has no meaning without the account
    /// it valued.
    private const string Sql = "DELETE FROM [InvestmentAccounts] WHERE [id] = @Id;";

    public async Task<Result> ExecuteAsync(DeleteInvestmentAccountRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(Sql, new { request.Id }, cancellationToken: ct));

        return affected == 0 ? Result.NotFound("Investment account not found") : Result.NoContent();
    }
}

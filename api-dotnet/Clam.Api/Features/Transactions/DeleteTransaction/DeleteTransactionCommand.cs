using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Transactions.DeleteTransaction;

public sealed class DeleteTransactionCommand(IDbConnectionFactory factory)
{
    private const string Sql = "DELETE FROM [Transactions] WHERE [id] = @Id;";

    public async Task<Result> ExecuteAsync(DeleteTransactionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(Sql, new { request.Id }, cancellationToken: ct));

        // Prisma's `delete` throws P2025 on a missing row and the Express route
        // lets that become a 500. A 404 is the honest answer, and the client
        // treats any non-2xx the same way.
        return affected == 0 ? Result.NotFound("Transaction not found") : Result.NoContent();
    }
}

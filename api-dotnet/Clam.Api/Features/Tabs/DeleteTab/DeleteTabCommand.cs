using Ardalis.Result;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Tabs.DeleteTab;

public sealed class DeleteTabCommand(IDbConnectionFactory factory)
{
    private const string Sql = "DELETE FROM [Tabs] WHERE [id] = @Id;";

    public async Task<Result> ExecuteAsync(DeleteTabRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var connection = await factory.OpenAsync(ct);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(Sql, new { request.Id }, cancellationToken: ct));

        return affected == 0 ? Result.NotFound("Tab not found") : Result.NoContent();
    }
}

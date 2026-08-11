using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Tabs.CreateTab;

public sealed class CreateTabCommand(IDbConnectionFactory factory, IIdGenerator ids)
{
    private const string Sql = $"""
        INSERT INTO [Tabs] ([id], [person], [description], [amount], [direction])
        OUTPUT {TabSql.Inserted}
        VALUES (@Id, @Person, @Description, @Amount, @Direction);
        """;

    public async Task<TabRecord> ExecuteAsync(CreateTabRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parameters = new
        {
            Id = ids.NewId(),
            request.Person,
            request.Description,
            request.Amount,
            Direction = request.Direction.ToString(),
        };

        using var connection = await factory.OpenAsync(ct);
        return await connection.QuerySingleAsync<TabRecord>(
            new CommandDefinition(Sql, parameters, cancellationToken: ct));
    }
}

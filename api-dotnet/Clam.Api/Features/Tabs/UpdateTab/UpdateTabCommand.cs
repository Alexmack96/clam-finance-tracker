using Ardalis.Result;
using Clam.Api.Domain;
using Clam.Api.Infrastructure.Data;
using Dapper;

namespace Clam.Api.Features.Tabs.UpdateTab;

public sealed class UpdateTabCommand(IDbConnectionFactory factory)
{
    /// `settledAt` follows `status` unless the caller set it explicitly:
    /// settling stamps now, reopening clears it. Deriving it from the status is
    /// what stops a reopened tab keeping the date it was settled on.
    private const string Sql = $"""
        UPDATE  [Tabs]
        SET     [person]      = COALESCE(@Person, [person]),
                [description] = COALESCE(@Description, [description]),
                [amount]      = COALESCE(@Amount, [amount]),
                [direction]   = COALESCE(@Direction, [direction]),
                [status]      = COALESCE(@Status, [status]),
                [settledAt]   = CASE
                                    WHEN @SettledAtProvided = 1 THEN @SettledAt
                                    WHEN @Status = 'Settled' THEN SYSUTCDATETIME()
                                    WHEN @Status = 'Open' THEN NULL
                                    ELSE [settledAt]
                                END,
                [updatedAt]   = SYSUTCDATETIME()
        OUTPUT  {TabSql.Inserted}
        WHERE   [id] = @Id;
        """;

    public async Task<Result<TabRecord>> ExecuteAsync(UpdateTabRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parameters = new
        {
            request.Id,
            request.Person,
            request.Description,
            request.Amount,
            Direction = request.Direction?.ToString(),
            Status = request.Status?.ToString(),
            request.SettledAt,
            SettledAtProvided = request.SettledAt is not null,
        };

        using var connection = await factory.OpenAsync(ct);
        var updated = await connection.QuerySingleOrDefaultAsync<TabRecord>(
            new CommandDefinition(Sql, parameters, cancellationToken: ct));

        return updated is null ? Result<TabRecord>.NotFound("Tab not found") : Result<TabRecord>.Success(updated);
    }
}

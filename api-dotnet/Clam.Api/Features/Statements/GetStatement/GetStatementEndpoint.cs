using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Statements.GetStatement;

public sealed class GetStatementEndpoint(GetStatementQuery query)
    : ResultEndpoint<GetStatementRequest, GetStatementResponse>
{
    public override void Configure()
    {
        Get("admin/statements/{Id}");
        Description(b => b.WithName("GetStatement"));
    }

    public override async Task HandleAsync(GetStatementRequest req, CancellationToken ct)
        => await SendResultAsync(await query.ExecuteAsync(req, ct), ct);
}

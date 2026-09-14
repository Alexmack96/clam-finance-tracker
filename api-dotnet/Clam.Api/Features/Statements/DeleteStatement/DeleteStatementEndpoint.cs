using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Statements.DeleteStatement;

public sealed class DeleteStatementEndpoint(DeleteStatementCommand command)
    : ResultEndpoint<DeleteStatementRequest, DeleteStatementResponse>
{
    public override void Configure()
    {
        Delete("admin/statements/{Id}");
        Description(b => b.WithName("DeleteStatement"));
    }

    public override async Task HandleAsync(DeleteStatementRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}

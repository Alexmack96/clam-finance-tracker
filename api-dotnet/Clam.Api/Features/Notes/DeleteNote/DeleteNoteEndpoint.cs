using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Notes.DeleteNote;

public sealed class DeleteNoteEndpoint(DeleteNoteCommand command) : ResultEndpointWithoutResponse<DeleteNoteRequest>
{
    public override void Configure()
    {
        Delete("notes/{Id}");
        AllowAnonymous();
        Description(b => b.WithName("DeleteNote"));
    }

    public override async Task HandleAsync(DeleteNoteRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}

using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Notes.UpdateNote;

public sealed class UpdateNoteEndpoint(UpdateNoteCommand command) : ResultEndpoint<UpdateNoteRequest, NoteRecord>
{
    public override void Configure()
    {
        Patch("notes/{Id}");
        AllowAnonymous();
        Description(b => b.WithName("UpdateNote"));
    }

    public override async Task HandleAsync(UpdateNoteRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}

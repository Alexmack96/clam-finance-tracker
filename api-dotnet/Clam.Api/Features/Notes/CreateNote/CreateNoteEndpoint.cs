using FastEndpoints;

namespace Clam.Api.Features.Notes.CreateNote;

public sealed class CreateNoteEndpoint(CreateNoteCommand command) : Endpoint<CreateNoteRequest, NoteRecord>
{
    public override void Configure()
    {
        Post("notes");
        Description(b => b.WithName("CreateNote"));
    }

    public override async Task HandleAsync(CreateNoteRequest req, CancellationToken ct)
    {
        // 201 with the body, no Location header — the Express route answers
        // `res.status(201).json(note)` and there is no per-note GET to point at.
        await Send.ResponseAsync(await command.ExecuteAsync(req, ct), StatusCodes.Status201Created, ct);
    }
}

using FastEndpoints;

namespace Clam.Api.Features.Notes.GetNotes;

public sealed class GetNotesEndpoint(GetNotesQuery query) : EndpointWithoutRequest<GetNotesResponse>
{
    public override void Configure()
    {
        Get("notes");
        Description(b => b.WithName("GetNotes"));
    }

    public override async Task HandleAsync(CancellationToken ct)
        => await Send.OkAsync(new GetNotesResponse(await query.ExecuteAsync(ct)), ct);
}

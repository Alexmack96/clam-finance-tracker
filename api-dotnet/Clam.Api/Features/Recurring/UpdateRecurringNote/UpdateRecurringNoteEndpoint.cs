using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Recurring.UpdateRecurringNote;

public sealed class UpdateRecurringNoteEndpoint(UpdateRecurringNoteCommand command)
    : ResultEndpoint<UpdateRecurringNoteRequest, RecurringVerdictRecord>
{
    public override void Configure()
    {
        Patch("recurring/note");
        AllowAnonymous();
        Description(b => b.WithName("UpdateRecurringNote"));
    }

    public override async Task HandleAsync(UpdateRecurringNoteRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}

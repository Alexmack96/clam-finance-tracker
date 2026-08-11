using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Recurring.SetRecurringVerdict;

public sealed class SetRecurringVerdictEndpoint(SetRecurringVerdictCommand command)
    : ResultEndpoint<SetRecurringVerdictRequest, RecurringVerdictRecord>
{
    public override void Configure()
    {
        Put("recurring/verdict");
        AllowAnonymous();
        Description(b => b.WithName("SetRecurringVerdict"));
    }

    public override async Task HandleAsync(SetRecurringVerdictRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}

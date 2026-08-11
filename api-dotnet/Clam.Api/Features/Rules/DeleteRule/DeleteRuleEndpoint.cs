using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Rules.DeleteRule;

public sealed class DeleteRuleEndpoint(DeleteRuleCommand command)
    : ResultEndpointWithoutResponse<DeleteRuleRequest>
{
    public override void Configure()
    {
        Delete("rules/{Id}");
        AllowAnonymous();
        Description(b => b.WithName("DeleteRule"));
    }

    public override async Task HandleAsync(DeleteRuleRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}

using Clam.Api.Domain.Rules;
using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Rules.UpdateRule;

public sealed class UpdateRuleEndpoint(UpdateRuleCommand command) : ResultEndpoint<UpdateRuleRequest, Rule>
{
    public override void Configure()
    {
        Patch("rules/{Id}");
        AllowAnonymous();
        Description(b => b.WithName("UpdateRule"));
    }

    public override async Task HandleAsync(UpdateRuleRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}

using Clam.Api.Domain.Rules;
using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Rules.CreateRule;

public sealed class CreateRuleEndpoint(CreateRuleCommand command) : ResultEndpoint<CreateRuleRequest, Rule>
{
    public override void Configure()
    {
        Post("rules");
        Description(b => b.WithName("CreateRule"));
    }

    public override async Task HandleAsync(CreateRuleRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}

using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Rules.ApplyRules;

public sealed class ApplyRulesEndpoint(ApplyRulesCommand command)
    : ResultEndpoint<ApplyRulesRequest, ApplyRulesResponse>
{
    public override void Configure()
    {
        Post("rules/apply");
        AllowAnonymous();
        Description(b => b.WithName("ApplyRules"));
    }

    public override async Task HandleAsync(ApplyRulesRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}

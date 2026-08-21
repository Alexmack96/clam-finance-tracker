using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Rules.ReorderRules;

public sealed class ReorderRulesEndpoint(ReorderRulesCommand command)
    : ResultEndpoint<ReorderRulesRequest, ReorderRulesResponse>
{
    public override void Configure()
    {
        Post("rules/reorder");
        Description(b => b.WithName("ReorderRules"));
    }

    public override async Task HandleAsync(ReorderRulesRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}

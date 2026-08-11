using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Rules.PreviewRules;

public sealed class PreviewRulesEndpoint(PreviewRulesQuery query)
    : ResultEndpoint<PreviewRulesRequest, PreviewRulesResponse>
{
    public override void Configure()
    {
        Post("rules/preview");
        AllowAnonymous();
        Description(b => b.WithName("PreviewRules"));
    }

    public override async Task HandleAsync(PreviewRulesRequest req, CancellationToken ct)
        => await SendResultAsync(await query.ExecuteAsync(req, ct), ct);
}

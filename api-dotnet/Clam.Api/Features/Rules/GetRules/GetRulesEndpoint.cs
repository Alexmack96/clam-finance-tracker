using FastEndpoints;

namespace Clam.Api.Features.Rules.GetRules;

public sealed class GetRulesEndpoint(GetRulesQuery query) : EndpointWithoutRequest<GetRulesResponse>
{
    public override void Configure()
    {
        Get("rules");
        AllowAnonymous();
        Description(b => b.WithName("GetRules"));
    }

    public override async Task HandleAsync(CancellationToken ct)
        => await Send.OkAsync(new GetRulesResponse(await query.ExecuteAsync(ct)), ct);
}

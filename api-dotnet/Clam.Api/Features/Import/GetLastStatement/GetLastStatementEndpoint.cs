using FastEndpoints;

namespace Clam.Api.Features.Import.GetLastStatement;

public sealed class GetLastStatementEndpoint(GetLastStatementQuery query)
    : EndpointWithoutRequest<GetLastStatementResponse>
{
    public override void Configure()
    {
        Get("admin/last-statement");
        AllowAnonymous();
        Description(b => b.WithName("GetLastStatement"));
    }

    public override async Task HandleAsync(CancellationToken ct)
        => await Send.OkAsync(await query.ExecuteAsync(ct), ct);
}

using FastEndpoints;

namespace Clam.Api.Features.Statements.GetStatements;

public sealed class GetStatementsEndpoint(GetStatementsQuery query)
    : EndpointWithoutRequest<GetStatementsResponse>
{
    public override void Configure()
    {
        Get("admin/statements");
        Description(b => b.WithName("GetStatements"));
    }

    public override async Task HandleAsync(CancellationToken ct)
        => await Send.OkAsync(new GetStatementsResponse(await query.ExecuteAsync(ct)), ct);
}

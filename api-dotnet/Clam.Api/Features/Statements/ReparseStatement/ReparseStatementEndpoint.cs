using Ardalis.Result;
using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Statements.ReparseStatement;

public sealed class ReparseStatementEndpoint(ReparseStatementCommand command)
    : Endpoint<ReparseStatementRequest, ReparseStatementResponse>
{
    public override void Configure()
    {
        Post("admin/statements/{Id}/reparse");
        Description(b => b.WithName("ReparseStatement"));
    }

    public override async Task HandleAsync(ReparseStatementRequest req, CancellationToken ct)
    {
        var result = await command.ExecuteAsync(req, ct);

        // Same override as the download slice: Unavailable is 503 in the shared
        // table, but a statement whose bytes have left the volume is gone for
        // good rather than temporarily unreachable.
        if (!result.IsSuccess && result.Status == ResultStatus.Unavailable)
        {
            await ResultProblem.WriteAsync(
                HttpContext, result.Status, result.Errors, result.ValidationErrors, ct,
                StatusCodes.Status410Gone);
            return;
        }

        await this.SendAsResultAsync(result, ct);
    }
}

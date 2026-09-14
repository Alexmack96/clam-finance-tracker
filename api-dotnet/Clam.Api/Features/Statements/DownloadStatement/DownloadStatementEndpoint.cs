using Ardalis.Result;
using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Statements.DownloadStatement;

/// The one endpoint that sends bytes rather than JSON, so it handles its own
/// success path instead of going through <c>ResultEndpoint</c> — that base class
/// serialises, which is exactly what a PDF must not be put through.
public sealed class DownloadStatementEndpoint(DownloadStatementQuery query)
    : Endpoint<DownloadStatementRequest>
{
    public override void Configure()
    {
        Get("admin/statements/{Id}/file");
        Description(b => b.WithName("DownloadStatement"));
    }

    public override async Task HandleAsync(DownloadStatementRequest req, CancellationToken ct)
    {
        var result = await query.ExecuteAsync(req, ct);

        if (!result.IsSuccess)
        {
            // Unavailable maps to 503 in the shared table, but a missing file is
            // permanently gone rather than temporarily unreachable, so this one
            // slice overrides it to 410. The override is passed *into* the
            // writer: it sets the status itself, so setting it here first only
            // looked like it worked.
            int? status = result.Status == ResultStatus.Unavailable
                ? StatusCodes.Status410Gone
                : null;

            await ResultProblem.WriteAsync(
                HttpContext, result.Status, result.Errors, result.ValidationErrors, ct, status);
            return;
        }

        // `attachment`, never `inline`, so a PDF cannot be rendered in the app's
        // own origin.
        await Send.BytesAsync(
            result.Value.Content,
            fileName: result.Value.FileName,
            contentType: "application/pdf",
            cancellation: ct);
    }
}

using Microsoft.AspNetCore.Http;

namespace Clam.Api.Features.Import.ImportAmex;

public sealed class ImportAmexRequest
{
    public IFormFile? File { get; set; }

    /// Amex is the one card shared between people, so the owner comes from the
    /// upload form rather than from the statement. Unrecognised values fall back
    /// to the default rather than failing: the owner is a label on the row, and
    /// rejecting a whole statement over it would be a worse trade.
    public string? Owner { get; set; }
}

using Microsoft.AspNetCore.Http;

namespace Clam.Api.Features.Import.ImportSofi;

public sealed class ImportSofiRequest
{
    public IFormFile? File { get; set; }

    /// Defaults to Casey, whose account this is. The two US accounts are the
    /// only ones that do not default to Alex.
    public string? Owner { get; set; }
}

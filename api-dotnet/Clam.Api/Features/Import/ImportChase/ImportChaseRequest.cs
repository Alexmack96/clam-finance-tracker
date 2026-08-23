using Microsoft.AspNetCore.Http;

namespace Clam.Api.Features.Import.ImportChase;

public sealed class ImportChaseRequest
{
    public IFormFile? File { get; set; }

    /// Defaults to Casey, whose card this is. The two US accounts are the only
    /// ones that do not default to Alex.
    public string? Owner { get; set; }
}

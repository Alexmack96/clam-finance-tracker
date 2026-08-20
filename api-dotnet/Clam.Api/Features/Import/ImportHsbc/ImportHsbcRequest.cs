using Microsoft.AspNetCore.Http;

namespace Clam.Api.Features.Import.ImportHsbc;

public sealed class ImportHsbcRequest
{
    public IFormFile? File { get; set; }

    /// Defaults to Joint rather than a person: this is the shared current
    /// account, so an unlabelled upload belongs to both by default. Amex, a card
    /// with a named holder, defaults to Alex instead.
    public string? Owner { get; set; }
}

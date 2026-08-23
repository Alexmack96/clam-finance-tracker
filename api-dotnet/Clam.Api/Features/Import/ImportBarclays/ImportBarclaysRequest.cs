using Microsoft.AspNetCore.Http;

namespace Clam.Api.Features.Import.ImportBarclays;

public sealed class ImportBarclaysRequest
{
    public IFormFile? File { get; set; }

    /// Defaults to Alex. This is one card with one named holder, so an
    /// unlabelled upload has only one person it can belong to — unlike HSBC's
    /// shared current account, which defaults to Joint.
    public string? Owner { get; set; }
}

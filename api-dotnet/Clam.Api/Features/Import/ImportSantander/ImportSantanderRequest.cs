using Microsoft.AspNetCore.Http;

namespace Clam.Api.Features.Import.ImportSantander;

public sealed class ImportSantanderRequest
{
    public IFormFile? File { get; set; }

    /// Defaults to Alex. This is a sole account, so an unlabelled upload has
    /// only one person it can belong to — unlike HSBC's shared current account,
    /// which defaults to Joint.
    public string? Owner { get; set; }
}

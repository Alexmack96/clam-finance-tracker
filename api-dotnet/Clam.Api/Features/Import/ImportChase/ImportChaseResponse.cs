namespace Clam.Api.Features.Import.ImportChase;

public sealed class ImportChaseResponse
{
    public int Imported { get; init; }

    public IReadOnlyList<string> Duplicates { get; init; } = [];

    public string StatementFileId { get; init; } = "";
}

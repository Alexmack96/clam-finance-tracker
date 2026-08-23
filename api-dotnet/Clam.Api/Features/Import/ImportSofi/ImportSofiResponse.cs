namespace Clam.Api.Features.Import.ImportSofi;

public sealed class ImportSofiResponse
{
    public int Imported { get; init; }

    public IReadOnlyList<string> Duplicates { get; init; } = [];

    public string StatementFileId { get; init; } = "";
}

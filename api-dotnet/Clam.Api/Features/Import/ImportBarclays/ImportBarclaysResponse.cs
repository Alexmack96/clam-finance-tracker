namespace Clam.Api.Features.Import.ImportBarclays;

public sealed class ImportBarclaysResponse
{
    public int Imported { get; init; }

    public IReadOnlyList<string> Duplicates { get; init; } = [];

    public string StatementFileId { get; init; } = "";
}

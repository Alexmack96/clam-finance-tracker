namespace Clam.Api.Features.Import.ImportSantander;

public sealed class ImportSantanderResponse
{
    public int Imported { get; init; }

    public IReadOnlyList<string> Duplicates { get; init; } = [];

    public string StatementFileId { get; init; } = "";
}

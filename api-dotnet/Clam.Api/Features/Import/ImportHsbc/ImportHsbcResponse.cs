namespace Clam.Api.Features.Import.ImportHsbc;

public sealed class ImportHsbcResponse
{
    public int Imported { get; init; }

    public IReadOnlyList<string> Duplicates { get; init; } = [];

    public string StatementFileId { get; init; } = "";
}

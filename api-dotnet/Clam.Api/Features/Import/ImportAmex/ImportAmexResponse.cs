namespace Clam.Api.Features.Import.ImportAmex;

public sealed class ImportAmexResponse
{
    public int Imported { get; init; }

    /// The ids recognised as already staged. Returned in full rather than
    /// counted: when a statement partially overlaps one already imported, these
    /// are what you look at to understand why.
    public IReadOnlyList<string> Duplicates { get; init; } = [];

    public string StatementFileId { get; init; } = "";
}

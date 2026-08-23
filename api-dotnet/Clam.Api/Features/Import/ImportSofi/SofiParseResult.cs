namespace Clam.Api.Features.Import.ImportSofi;

/// Either the rows, or the status and message to reject the upload with. Mirrors
/// the other banks' equivalents: a PDF that cannot be read (400) is a different
/// answer from one that read fine but does not add up (422), and collapsing both
/// to "invalid" would lose the distinction that matters.
public sealed record SofiParseResult
{
    private SofiParseResult() { }

    public bool Ok { get; private init; }

    public IReadOnlyList<SofiRow> Rows { get; private init; } = [];

    public string? StatementDate { get; private init; }

    public int Status { get; private init; }

    public string? Error { get; private init; }

    public static SofiParseResult Parsed(IReadOnlyList<SofiRow> rows, string statementDate) =>
        new() { Ok = true, Rows = rows, StatementDate = statementDate };

    public static SofiParseResult Rejected(int status, string error) =>
        new() { Ok = false, Status = status, Error = error };
}

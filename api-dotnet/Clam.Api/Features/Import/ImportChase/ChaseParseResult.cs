namespace Clam.Api.Features.Import.ImportChase;

/// Either the rows, or the status and message to reject the upload with. Mirrors
/// the other banks' equivalents: a PDF that cannot be read (400) is a different
/// answer from one that read fine but does not add up (422), and collapsing both
/// to "invalid" would lose the distinction that matters.
public sealed record ChaseParseResult
{
    private ChaseParseResult() { }

    public bool Ok { get; private init; }

    public IReadOnlyList<ChaseRow> Rows { get; private init; } = [];

    public string? StatementDate { get; private init; }

    public int Status { get; private init; }

    public string? Error { get; private init; }

    public static ChaseParseResult Parsed(IReadOnlyList<ChaseRow> rows, string statementDate) =>
        new() { Ok = true, Rows = rows, StatementDate = statementDate };

    public static ChaseParseResult Rejected(int status, string error) =>
        new() { Ok = false, Status = status, Error = error };
}

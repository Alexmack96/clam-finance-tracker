namespace Clam.Api.Features.Import.ImportAmex;

/// Either the rows, or the status and message to reject the upload with.
///
/// Not <c>Result&lt;T&gt;</c>: the parser distinguishes a PDF it cannot read
/// (400) from one it read fine but will not vouch for (422), and that split is
/// the whole point — the second is the interesting one, and collapsing both to
/// "invalid" would lose it. Returning the status keeps the upload route and the
/// (not yet ported) re-parse route on one definition of "is this statement
/// acceptable".
public sealed record AmexParseResult
{
    private AmexParseResult() { }

    public bool Ok { get; private init; }

    public IReadOnlyList<AmexRow> Rows { get; private init; } = [];

    public string? StatementDate { get; private init; }

    public int Status { get; private init; }

    public string? Error { get; private init; }

    public static AmexParseResult Parsed(IReadOnlyList<AmexRow> rows, string statementDate) =>
        new() { Ok = true, Rows = rows, StatementDate = statementDate };

    public static AmexParseResult Rejected(int status, string error) =>
        new() { Ok = false, Status = status, Error = error };
}

namespace Clam.Api.Features.Import.ImportSantander;

/// Either the rows, or the status and message to reject the upload with. Mirrors
/// the Amex, Barclays and HSBC equivalents: a PDF that cannot be read (400) is a
/// different answer from one that read fine but does not add up (422), and
/// collapsing both to "invalid" would lose the distinction that matters.
public sealed record SantanderParseResult
{
    private SantanderParseResult() { }

    public bool Ok { get; private init; }

    public IReadOnlyList<SantanderRow> Rows { get; private init; } = [];

    public string? StatementDate { get; private init; }

    public int Status { get; private init; }

    public string? Error { get; private init; }

    public static SantanderParseResult Parsed(IReadOnlyList<SantanderRow> rows, string statementDate) =>
        new() { Ok = true, Rows = rows, StatementDate = statementDate };

    public static SantanderParseResult Rejected(int status, string error) =>
        new() { Ok = false, Status = status, Error = error };
}

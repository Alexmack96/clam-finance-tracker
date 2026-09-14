namespace Clam.Api.Features.Statements.ReparseStatement;

/// What the old parse accounted for, and is now gone.
public sealed class RemovedByReparse
{
    public int Staged { get; set; }

    public int Transactions { get; set; }
}

/// Deliberately the upload response with a <c>removed</c> block on the front:
/// a re-parse is an import of a statement already held, and reporting it in a
/// different shape would make the two impossible to compare.
public sealed class ReparseStatementResponse
{
    public RemovedByReparse Removed { get; set; } = new();

    public int Imported { get; set; }

    /// Rows whose ids belong to a *different* statement file. After this
    /// statement's own rows have been cleared these are the only duplicates
    /// left, so a non-empty list means two statements genuinely overlap.
    public IReadOnlyList<string> Duplicates { get; set; } = [];
}

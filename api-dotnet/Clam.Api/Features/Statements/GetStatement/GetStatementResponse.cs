namespace Clam.Api.Features.Statements.GetStatement;

/// One staged row, exactly as it came off the statement. Amounts stay strings
/// here — this view exists to show what the parser read, and coercing it to a
/// number would hide the rows that are the reason you are looking.
///
/// It is one shape across banks because the question it answers is "what did
/// this document produce", which is per-document and not per-table. A statement
/// file belongs to exactly one bank, so a given response is all one bank's rows
/// and the fields the other bank contributes are simply null: Amex has no
/// payment type or running balance, HSBC has no process date or foreign side.
public sealed class StagedStatementRow
{
    public string TransactionId { get; set; } = "";
    public string TransactionDate { get; set; } = "";

    /// Amex only — HSBC prints one date per row.
    public string? ProcessDate { get; set; }
    public string Description { get; set; } = "";

    /// The figure, whichever column it was printed in. For HSBC the column is
    /// the whole of the direction, which <see cref="IsCredit"/> carries.
    public string Amount { get; set; } = "";
    public bool IsCredit { get; set; }
    public string? ForeignCurrency { get; set; }
    public string? ForeignAmount { get; set; }

    /// HSBC only — the payment-type code that opened the row (BP, OBP, VIS…).
    public string? PaymentType { get; set; }

    /// HSBC only — the running balance after the row.
    public string? Balance { get; set; }
    public string StatementDate { get; set; } = "";
    public string Owner { get; set; } = "";
    public DateTime ImportedAt { get; set; }
    public string Status { get; set; } = "";
    public string? StatementFileId { get; set; }
}

public sealed class GetStatementResponse
{
    public string Id { get; set; } = "";
    public string Bank { get; set; } = "";
    public string Owner { get; set; } = "";
    public string? StatementDate { get; set; }
    public string OriginalName { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public int ByteSize { get; set; }
    public string StorageKey { get; set; } = "";
    public DateTime UploadedAt { get; set; }
    public int? RowCount { get; set; }
    public bool Reconciled { get; set; }

    public int StagedRows { get; set; }

    /// How many normalised transactions this statement produced. Compared
    /// against <see cref="StagedRows"/> it is how you spot a statement that
    /// staged but never processed.
    public int Transactions { get; set; }

    public IReadOnlyList<StagedStatementRow> Rows { get; set; } = [];
}

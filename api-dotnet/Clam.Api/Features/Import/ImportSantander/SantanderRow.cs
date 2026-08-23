namespace Clam.Api.Features.Import.ImportSantander;

/// One entry off a Santander current-account statement, in the statement's own
/// terms.
///
/// Money is split into <see cref="MoneyIn"/> and <see cref="MoneyOut"/> rather
/// than an amount plus a direction flag, because that is how the statement
/// prints it — two separate columns — and which column a figure sat in is the
/// only reliable statement of direction. The same shape HSBC's current account
/// uses, and for the same reason.
public sealed record SantanderRow
{
    /// ISO. The statement prints "21st Jan" with no year, so the year is
    /// inferred from the statement period's own end month.
    public required string Date { get; init; }

    public required string Description { get; init; }

    public string? MoneyIn { get; init; }

    public string? MoneyOut { get; init; }

    /// The running balance after this entry, as printed. Santander prints one on
    /// every row, which is what lets each row's direction be checked against the
    /// balance it moved to rather than merely read off a column.
    public required string Balance { get; init; }

    /// "21 Jan 2026 to 20 Feb 2026", off the statement period line.
    public required string StatementDate { get; init; }
}

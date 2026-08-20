namespace Clam.Api.Features.Import.ImportHsbc;

/// One transaction off an HSBC statement, in the statement's own terms.
///
/// Money is split into <see cref="MoneyOut"/> and <see cref="MoneyIn"/> rather
/// than an amount plus a direction flag, because that is how the statement
/// prints it — two separate columns — and which column a figure sat in is the
/// only reliable statement of direction. The payment type is *not*: some
/// incoming payments arrive typed BP rather than CR.
public sealed record HsbcRow
{
    public required string Date { get; init; }

    /// BP, OBP, CR, DD, SO, TFR, VIS, ATM, CHQ, FP, DEB, BGC, STO or DR.
    public required string PaymentType { get; init; }

    public required string Description { get; init; }

    public string? MoneyOut { get; set; }

    public string? MoneyIn { get; set; }

    /// The running balance, printed only on the last row of each date group, so
    /// most rows have none.
    public string? Balance { get; set; }

    public required string StatementDate { get; init; }
}

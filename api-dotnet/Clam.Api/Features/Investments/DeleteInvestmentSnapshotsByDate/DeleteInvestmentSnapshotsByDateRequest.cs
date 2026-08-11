namespace Clam.Api.Features.Investments.DeleteInvestmentSnapshotsByDate;

/// Deletes a whole column off the grid — every account's snapshot for one date,
/// scoped to one owner. That scoping is load-bearing: the two people share a
/// date axis but not a portfolio.
public sealed class DeleteInvestmentSnapshotsByDateRequest
{
    public DateTime Date { get; set; }
    public string? Owner { get; set; }
}

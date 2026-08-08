namespace Clam.Api.Features.Dashboard.GetDashboardSummary;

/// The two scalars the summary is derived from, as returned by the first result
/// set of the query.
///
/// Not a ValueTuple: tuple element names are compile-time metadata only, so
/// Dapper has nothing to match the column names against at runtime and would
/// silently hand back zeros.
public sealed record SummaryTotals(decimal CaseyIn, decimal JointExpenses);

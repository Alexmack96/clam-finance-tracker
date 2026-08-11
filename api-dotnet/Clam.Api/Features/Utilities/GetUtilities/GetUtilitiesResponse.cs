namespace Clam.Api.Features.Utilities.GetUtilities;

public sealed class UtilityPayment
{
    /// A decimal, so the shared converter renders it — a string on the wire, at
    /// Prisma's trimmed scale. Formatting it here by hand produced the column's
    /// scale instead ("1400.00" where Prisma sends "1400").
    public decimal Amount { get; set; }

    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
}

/// Payments split by who paid, keyed by owner *name* — `Alex`, `Casey`, `Joint`.
///
/// A dictionary rather than three properties, because the keys are the owner
/// enum's members and the serialiser camel-cases property names: typed
/// properties would put `alex` on the wire where the Express route puts `Alex`,
/// and the client indexes this by the owner value it already holds.
///
/// All three keys are always present. An owner who paid nothing this month still
/// needs an empty column rather than a missing one.
public sealed class UtilityPayers : Dictionary<string, List<UtilityPayment>>
{
    public UtilityPayers() : base(StringComparer.Ordinal) { }
}

public sealed class UtilityView
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public UtilityPayers Payments { get; set; } = new();

    /// A JSON number, not a string: the Express route sums with `Number(...)`
    /// and sends the result raw.
    public double TotalThisMonth { get; set; }
}

public sealed class GetUtilitiesResponse
{
    /// Formatted for display ("August 2026") — the client prints it verbatim.
    public string Month { get; set; } = "";

    public IReadOnlyList<UtilityView> Utilities { get; set; } = [];
}

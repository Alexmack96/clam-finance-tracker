using Clam.Api.Domain;

namespace Clam.Api.Features.Investments;

/// An account row. Feature-shared (tier 2): create and update both return it,
/// and the list slice returns a trimmed projection of it instead — which is why
/// that one has its own type rather than reusing this.
public sealed class InvestmentAccountRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// pension | crypto | equity | cash | commodity | debt. A string rather than
    /// an enum because it is a CHECK constraint the client also hardcodes, and
    /// adding one must not require a deploy of this service to *read* rows.
    public string Category { get; set; } = "";

    public Owner Owner { get; set; }
    public double? Rate { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class InvestmentSnapshotRecord
{
    public string Id { get; set; } = "";
    public string AccountId { get; set; } = "";
    public DateTime Date { get; set; }
    public double Value { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

internal static class InvestmentSql
{
    internal const string AccountInserted = """
        INSERTED.[id], INSERTED.[name], INSERTED.[category], INSERTED.[owner],
        INSERTED.[rate], INSERTED.[sortOrder], INSERTED.[createdAt], INSERTED.[updatedAt]
        """;
}

/// The owner query parameter, resolved the same way everywhere it appears.
/// An unrecognised value falls back to Alex rather than becoming a 400 — the
/// Express routes do the same, and the client always sends a valid one.
public static class InvestmentOwner
{
    public static Owner Parse(string? value) =>
        Enum.TryParse<Owner>(value, ignoreCase: false, out var owner) ? owner : Owner.Alex;
}

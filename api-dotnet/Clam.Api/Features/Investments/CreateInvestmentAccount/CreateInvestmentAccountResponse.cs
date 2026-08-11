using Clam.Api.Domain;

namespace Clam.Api.Features.Investments.CreateInvestmentAccount;

/// The created account with an empty snapshots array, matching Prisma's
/// `include: { snapshots: true }` on the create.
///
/// Written out rather than derived from <see cref="InvestmentAccountRecord"/>:
/// inheritance would leave the JSON property order at the serialiser's
/// discretion, and the array is the one thing this response adds.
public sealed class CreateInvestmentAccountResponse
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public Owner Owner { get; set; }
    public double? Rate { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// Always empty — the client relies on the key existing, not on its contents.
    public IReadOnlyList<InvestmentSnapshotRecord> Snapshots { get; } = [];
}

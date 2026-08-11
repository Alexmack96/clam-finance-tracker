using Clam.Api.Domain;

namespace Clam.Api.Features.Recurring;

/// A stored verdict row. Feature-shared (tier 2): the set-verdict and the
/// add-note slices both return exactly this, and the list slice reads it to
/// decorate a series.
public sealed class RecurringVerdictRecord
{
    public string Id { get; set; } = "";
    public Owner Owner { get; set; }
    public string Description { get; set; } = "";
    public RecurringStatus Status { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// Joint is deliberately unsupported: a recurring commitment is something a
/// person is on the hook for, and nothing in the data carries owner = Joint.
public static class RecurringOwner
{
    public static Owner Parse(string? value) =>
        Enum.TryParse<Owner>(value, ignoreCase: false, out var owner) && owner != Domain.Owner.Joint
            ? owner
            : Domain.Owner.Alex;

    public static bool IsSupported(Owner owner) => owner != Domain.Owner.Joint;
}

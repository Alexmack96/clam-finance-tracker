using Clam.Api.Domain;

namespace Clam.Api.Features.Tabs;

/// A tab row. Feature-shared (tier 2) on the rule of three: list, create and
/// update all return exactly this.
public sealed class TabRecord
{
    public string Id { get; set; } = "";
    public string Person { get; set; } = "";
    public string Description { get; set; } = "";

    /// Serialised as a string, like every other Decimal — see
    /// Infrastructure/Json/DecimalAsStringConverter.
    public decimal Amount { get; set; }

    public TabDirection Direction { get; set; }
    public TabStatus Status { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? SettledAt { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

internal static class TabSql
{
    internal const string Columns = """
        [id], [person], [description], [amount], [direction], [status],
        [dueDate], [settledAt], [note], [createdAt], [updatedAt]
        """;

    internal const string Inserted = """
        INSERTED.[id], INSERTED.[person], INSERTED.[description], INSERTED.[amount],
        INSERTED.[direction], INSERTED.[status], INSERTED.[dueDate], INSERTED.[settledAt],
        INSERTED.[note], INSERTED.[createdAt], INSERTED.[updatedAt]
        """;
}

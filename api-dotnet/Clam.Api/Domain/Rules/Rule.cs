namespace Clam.Api.Domain.Rules;

/// An ordered auto-assignment rule, exactly as the database stores it and
/// exactly as it goes on the wire.
///
/// This lives in Domain/ rather than in a slice for the same reason
/// <see cref="Clam.Api.Domain.Enums"/> does: two features run these rules — the
/// Rules dry-run and the import pipeline's process step — and a rule that means
/// one thing in a preview and another during an unattended import is the single
/// worst bug this service could have. The definition is therefore owned in one
/// place, which is what `@clam/core` does on the Express side.
///
/// Property order is JSON property order and mirrors Prisma's model field for
/// field, so the response stays byte-comparable with the Express API.
public sealed class Rule
{
    public string Id { get; set; } = "";
    public RuleKind Kind { get; set; }
    public int Position { get; set; }
    public RuleJoin JoinOperator { get; set; }

    /// null means "any bank". Free text rather than an enum: it is matched
    /// against the namespace parsed out of `externalId`, and a bank the client
    /// has never heard of must fail to match rather than fail to deserialise.
    public string? Bank { get; set; }

    /// Output for <see cref="RuleKind.Category"/>; null on a Bucket rule.
    public string? CategoryId { get; set; }

    /// Output for <see cref="RuleKind.Bucket"/>; null on a Category rule.
    public Bucket? Bucket { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<RuleCondition> Conditions { get; set; } = [];
}

public sealed class RuleCondition
{
    public string Id { get; set; } = "";
    public RuleField Field { get; set; }
    public RuleOperator Operator { get; set; }
    public string Value { get; set; } = "";
    public bool Negate { get; set; }
    public int Position { get; set; }
}

/// The minimum a transaction has to expose to be matched. Rules never see a
/// whole transaction, which is what lets a draft rule be previewed against rows
/// that have not been loaded in full.
/// <param name="CategoryName">Category name, or null while Category rules are still being resolved.</param>
/// <param name="Bank">Namespace parsed from <c>externalId</c> (<c>monzo</c>, <c>amex</c>, …), or null.</param>
public readonly record struct MatchableTransaction(
    string Description,
    TransactionType Type,
    string? CategoryName,
    string? Bank);

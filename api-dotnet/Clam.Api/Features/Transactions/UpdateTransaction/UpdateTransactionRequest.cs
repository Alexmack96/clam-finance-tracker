using System.Text.Json;
using Clam.Api.Domain;

namespace Clam.Api.Features.Transactions.UpdateTransaction;

/// A PATCH, so every field is optional and null means "leave it alone".
///
/// `Note` is the one field where that is not enough: the Express schema accepts
/// an explicit null to *clear* a note, and deserialising into `string?` collapses
/// "absent" and "null" into the same value. Binding it as a raw
/// <see cref="JsonElement"/> keeps them apart — an absent property leaves the
/// struct at its default, whose ValueKind is Undefined, while an explicit null
/// parses to ValueKind Null.
public sealed class UpdateTransactionRequest
{
    public string Id { get; set; } = "";

    public JsonElement Note { get; set; }

    public string? CategoryId { get; set; }
    public Owner? Owner { get; set; }
    public bool? Reviewed { get; set; }

    /// The Bucket is the source of truth for savings maths. Once set it can only
    /// be flipped between the four — the client never sends null to clear it.
    public Bucket? Bucket { get; set; }

    /// Explicit unpin — hands the field back to the rules engine.
    public bool? CategoryPinned { get; set; }
    public bool? BucketPinned { get; set; }

    /// True when the caller sent a `note` key at all, whatever its value.
    internal bool NoteProvided => Note.ValueKind != JsonValueKind.Undefined;

    /// The note to store: the string when one was sent, null when the caller
    /// sent an explicit null. Meaningless unless <see cref="NoteProvided"/>.
    internal string? NoteValue => Note.ValueKind == JsonValueKind.String ? Note.GetString() : null;
}

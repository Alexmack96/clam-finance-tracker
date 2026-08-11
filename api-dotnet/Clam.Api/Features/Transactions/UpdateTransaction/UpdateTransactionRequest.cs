using System.Text.Json;
using Clam.Api.Domain;
using FastEndpoints;
using FluentValidation;

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

/// Most of this request is enums and bools the binder has already vetted. Two
/// fields reach here able to be well-formed and still wrong, and both write
/// silently rather than failing loudly if they are not checked.
public sealed class UpdateTransactionValidator : Validator<UpdateTransactionRequest>
{
    public UpdateTransactionValidator()
    {
        // Longer than an id column is not a missing category, it is not an id at
        // all — and the UPDATE COALESCEs it straight into the column, where the
        // truncation error is a 500. See <see cref="Ids"/>.
        RuleFor(x => x.CategoryId!)
            .NotEmpty().WithMessage("categoryId cannot be blank")
            .MaximumLength(Ids.MaxLength).WithMessage("categoryId is not an id")
            .When(x => x.CategoryId is not null);

        // `Note` binds as a raw JsonElement so an explicit null can be told from
        // an absent key, which means the binder accepts any JSON shape at all.
        // Without this rule an object or a number takes the "not a string" branch
        // of NoteValue, lands as null, and *clears* the note — a destructive
        // write in response to a malformed request, answered 200.
        //
        // No length rule: the column is NVARCHAR(MAX).
        RuleFor(x => x.Note)
            .Must(note => note.ValueKind is JsonValueKind.String or JsonValueKind.Null)
            .WithMessage("note must be a string or null")
            .When(x => x.NoteProvided);
    }
}

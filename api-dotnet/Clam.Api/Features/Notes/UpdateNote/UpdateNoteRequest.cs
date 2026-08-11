using Clam.Api.Features.Notes.CreateNote;
using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Notes.UpdateNote;

/// A PATCH: null means "leave it alone". `Body` is nullable in the database too,
/// so clearing it is not expressible here — the same gap the Express route has.
public sealed class UpdateNoteRequest
{
    public string Id { get; set; } = "";
    public string? Title { get; set; }
    public string? Body { get; set; }
    public bool? Pinned { get; set; }
}

public sealed class UpdateNoteValidator : Validator<UpdateNoteRequest>
{
    public UpdateNoteValidator()
    {
        RuleFor(x => x.Title!)
            .NotEmpty()
            .MaximumLength(CreateNoteValidator.MaxTitleLength).WithMessage("Title is too long")
            .When(x => x.Title is not null);
    }
}

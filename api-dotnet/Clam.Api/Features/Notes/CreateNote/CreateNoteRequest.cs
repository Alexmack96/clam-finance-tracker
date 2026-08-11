using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Notes.CreateNote;

public sealed class CreateNoteRequest
{
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public bool Pinned { get; set; }
}

public sealed class CreateNoteValidator : Validator<CreateNoteRequest>
{
    /// The width of `Notes.title` in db/schema.sql. Past it SQL Server raises a
    /// truncation error, which surfaces as a 500 for what is the caller's
    /// mistake. `Body` needs no equivalent — that column is NVARCHAR(MAX).
    internal const int MaxTitleLength = 300;

    public CreateNoteValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(MaxTitleLength).WithMessage("Title is too long");
    }
}

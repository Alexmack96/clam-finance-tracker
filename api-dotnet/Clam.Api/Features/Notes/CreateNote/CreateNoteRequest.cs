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
    public CreateNoteValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Title is required");
    }
}

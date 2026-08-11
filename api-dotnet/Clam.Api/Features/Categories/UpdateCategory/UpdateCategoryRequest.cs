using Clam.Api.Features.Categories.CreateCategory;
using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Categories.UpdateCategory;

/// A PATCH, so every field is optional and null means "leave it alone". That is
/// why these are nullable rather than defaulted: an empty string is a value the
/// caller sent and must fail validation, whereas an absent field is not.
public sealed class UpdateCategoryRequest
{
    public string Id { get; set; } = "";
    public string? Name { get; set; }
    public string? Color { get; set; }
}

public sealed class UpdateCategoryValidator : Validator<UpdateCategoryRequest>
{
    public UpdateCategoryValidator()
    {
        // Trimmed before it is measured, matching the command and the create
        // slice. See the note on CreateCategoryValidator.
        RuleFor(x => x.Name!)
            .NotEmpty()
            .Must(name => name.Trim().Length <= CreateCategoryValidator.MaxNameLength)
            .WithMessage("Name is too long")
            .When(x => x.Name is not null);

        RuleFor(x => x.Color!)
            .Matches(CreateCategoryValidator.HexColorPattern)
            .WithMessage("Must be a hex colour like #14b8a6")
            .When(x => x.Color is not null);
    }
}

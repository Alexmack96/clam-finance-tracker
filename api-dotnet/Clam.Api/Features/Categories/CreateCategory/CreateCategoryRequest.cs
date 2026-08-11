using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Categories.CreateCategory;

public sealed class CreateCategoryRequest
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
}

/// The .NET half of `createCategorySchema` in `@clam/core`. Kept in the same
/// file as the request because the two are one idea — what a valid create looks
/// like — and splitting them means reading two files to answer one question.
///
/// FastEndpoints discovers this by reflection; there is no filter to register.
public sealed class CreateCategoryValidator : Validator<CreateCategoryRequest>
{
    /// A 6-digit hex colour, e.g. "#14b8a6".
    internal const string HexColorPattern = "^#[0-9a-fA-F]{6}$";

    public CreateCategoryValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MaximumLength(40).WithMessage("Name is too long");

        RuleFor(x => x.Color)
            .Matches(HexColorPattern).WithMessage("Must be a hex colour like #14b8a6");
    }
}

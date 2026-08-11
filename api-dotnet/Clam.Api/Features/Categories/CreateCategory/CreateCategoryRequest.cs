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

    /// Well inside `Categories.name` (NVARCHAR(100)). The bound is what fits a
    /// legend and a filter chip, not what fits the column.
    internal const int MaxNameLength = 40;

    public CreateCategoryValidator()
    {
        // Measured trimmed, because the command stores it trimmed. Checking the
        // raw string instead rejects a 40-character name typed with a trailing
        // space — the two ends have to agree on which string is the name.
        //
        // `Must` rather than `MaximumLength` on a transformed value: FluentValidation
        // removed `Transform` in 12.0, which is the version FastEndpoints 8.2 brings.
        // `NotEmpty` needs no such help — it already counts whitespace as empty.
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .Must(name => name.Trim().Length <= MaxNameLength).WithMessage("Name is too long");

        RuleFor(x => x.Color)
            .Matches(HexColorPattern).WithMessage("Must be a hex colour like #14b8a6");
    }
}

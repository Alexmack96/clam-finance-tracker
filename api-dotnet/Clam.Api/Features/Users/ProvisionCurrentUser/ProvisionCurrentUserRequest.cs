using Clam.Api.Domain;
using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Users.ProvisionCurrentUser;

public sealed class ProvisionCurrentUserRequest
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";

    /// Alex, Casey or Joint. Which person's figures the client defaults to, not
    /// a permission: every signed-in user reads every owner's data.
    public string? Owner { get; set; }
}

public sealed class ProvisionCurrentUserValidator : Validator<ProvisionCurrentUserRequest>
{
    /// The widths of `Users.name` and `Users.email` in db/schema.sql. 320 is also
    /// the RFC 5321 maximum for an address, which is where the column got it.
    internal const int MaxNameLength = 200;
    internal const int MaxEmailLength = 320;

    public ProvisionCurrentUserValidator()
    {
        RuleFor(x => x.Name)
            .MinimumLength(3).WithMessage("Name must be at least 3 characters")
            .MaximumLength(MaxNameLength).WithMessage("Name is too long");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Invalid email address")
            .MaximumLength(MaxEmailLength).WithMessage("Email is too long");

        // Matches CK_Users_owner. A value the check constraint would reject is a
        // 400 here rather than a 500 out of the database.
        RuleFor(x => x.Owner)
            .Must(o => o is null || Enum.TryParse<Owner>(o, ignoreCase: false, out _))
            .WithMessage($"Owner must be one of {string.Join(", ", Enum.GetNames<Owner>())}");
    }
}

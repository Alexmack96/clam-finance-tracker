using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Users.CreateUser;

public sealed class CreateUserRequest
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class CreateUserValidator : Validator<CreateUserRequest>
{
    /// The widths of `Users.name` and `Users.email` in db/schema.sql. 320 is also
    /// the RFC 5321 maximum for an address, which is where the column got it.
    /// `Password` is not capped here: it is hashed before it is stored, so its
    /// length on the wire has nothing to do with the column's.
    internal const int MaxNameLength = 200;
    internal const int MaxEmailLength = 320;

    public CreateUserValidator()
    {
        RuleFor(x => x.Name)
            .MinimumLength(3).WithMessage("Name must be at least 3 characters")
            .MaximumLength(MaxNameLength).WithMessage("Name is too long");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Invalid email address")
            .MaximumLength(MaxEmailLength).WithMessage("Email is too long");

        RuleFor(x => x.Password).MinimumLength(8).WithMessage("Password must be at least 8 characters");
    }
}

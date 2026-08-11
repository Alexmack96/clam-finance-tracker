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
    internal const int MaxNameLength = 200;
    internal const int MaxEmailLength = 320;

    /// Not a column width — the password is hashed before it is stored, so the
    /// column never sees it. It is a bound on work: scrypt at Better Auth's cost
    /// parameters is deliberately expensive, and hashing a megabyte of it is CPU
    /// this process spends on one unauthenticated request. 128 is past any
    /// passphrase a person types and any password a manager generates.
    internal const int MaxPasswordLength = 128;

    public CreateUserValidator()
    {
        RuleFor(x => x.Name)
            .MinimumLength(3).WithMessage("Name must be at least 3 characters")
            .MaximumLength(MaxNameLength).WithMessage("Name is too long");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Invalid email address")
            .MaximumLength(MaxEmailLength).WithMessage("Email is too long");

        RuleFor(x => x.Password)
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .MaximumLength(MaxPasswordLength)
            .WithMessage($"Password must be {MaxPasswordLength} characters or fewer");
    }
}

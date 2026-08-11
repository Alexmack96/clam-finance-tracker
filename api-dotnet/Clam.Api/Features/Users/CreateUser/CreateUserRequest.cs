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
    public CreateUserValidator()
    {
        RuleFor(x => x.Name).MinimumLength(3).WithMessage("Name must be at least 3 characters");
        RuleFor(x => x.Email).EmailAddress().WithMessage("Invalid email address");
        RuleFor(x => x.Password).MinimumLength(8).WithMessage("Password must be at least 8 characters");
    }
}

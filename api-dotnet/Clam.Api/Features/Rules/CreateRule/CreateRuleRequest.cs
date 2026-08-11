using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Rules.CreateRule;

/// The request *is* the rule body. Inheriting <see cref="RuleInput"/> rather
/// than wrapping it keeps the JSON flat, which is what the Express route accepts.
public sealed class CreateRuleRequest : RuleInput;

public sealed class CreateRuleValidator : Validator<CreateRuleRequest>
{
    public CreateRuleValidator()
    {
        Include(new RuleInputValidator());
    }
}

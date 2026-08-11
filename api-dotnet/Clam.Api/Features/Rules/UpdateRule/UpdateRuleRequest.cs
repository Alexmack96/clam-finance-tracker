using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Rules.UpdateRule;

/// An update carries the whole rule, not a patch: the client edits the
/// condition set as one thing, and a partial diff would need stable client-side
/// condition ids that do not exist.
public sealed class UpdateRuleRequest : RuleInput
{
    public string Id { get; set; } = "";
}

public sealed class UpdateRuleValidator : Validator<UpdateRuleRequest>
{
    public UpdateRuleValidator()
    {
        Include(new RuleInputValidator());
    }
}

using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Dev.SeedData;

/// FastEndpoints finds this by reflection and runs it before HandleAsync — no
/// filter to register and no `AddValidatorsFromAssembly` call, which is why
/// PremPoints' hand-rolled ValidationFilter&lt;T&gt; has no counterpart here.
/// Failures come back as RFC 7807 because Program.cs sets Errors.UseProblemDetails().
///
/// Note which slice gets a validator and which does not: this one, because Count
/// drives an allocation and a loop. GetTransactionsRequest deliberately has none —
/// see the comment on that type, an unknown filter value has to return an empty
/// list rather than a 400 to stay compatible with the Express route.
public sealed class SeedDataValidator : Validator<SeedDataRequest>
{
    /// Above this the request spends longer inserting than most proxies will
    /// wait, and the seeder is a dev convenience, not a load generator.
    public const int MaxCount = 20_000;

    public SeedDataValidator()
    {
        RuleFor(x => x.Count)
            .GreaterThan(0)
            .WithMessage("Count must be at least 1.")
            .LessThanOrEqualTo(MaxCount)
            .WithMessage($"Count must be {MaxCount:N0} or fewer.");
    }
}

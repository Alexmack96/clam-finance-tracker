using Clam.Api.Domain;
using FastEndpoints;
using FluentValidation;

namespace Clam.Api.Features.Tabs.UpdateTab;

public sealed class UpdateTabRequest
{
    public string Id { get; set; } = "";
    public string? Person { get; set; }
    public string? Description { get; set; }
    public decimal? Amount { get; set; }
    public TabDirection? Direction { get; set; }
    public TabStatus? Status { get; set; }

    /// Rarely sent: settling a tab normally just sets `status`, and the command
    /// stamps the time. This exists for backdating one.
    public DateTime? SettledAt { get; set; }
}

public sealed class UpdateTabValidator : Validator<UpdateTabRequest>
{
    public UpdateTabValidator()
    {
        RuleFor(x => x.Person!).NotEmpty().When(x => x.Person is not null);
        RuleFor(x => x.Description!).NotEmpty().When(x => x.Description is not null);
        RuleFor(x => x.Amount!.Value).GreaterThan(0).When(x => x.Amount is not null);
    }
}

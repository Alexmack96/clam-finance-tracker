using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Investments.UpdateInvestmentAccount;

public sealed class UpdateInvestmentAccountEndpoint(UpdateInvestmentAccountCommand command)
    : ResultEndpoint<UpdateInvestmentAccountRequest, InvestmentAccountRecord>
{
    public override void Configure()
    {
        Patch("investments/accounts/{Id}");
        Description(b => b.WithName("UpdateInvestmentAccount"));
    }

    public override async Task HandleAsync(UpdateInvestmentAccountRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}

using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Investments.CreateInvestmentAccount;

public sealed class CreateInvestmentAccountEndpoint(CreateInvestmentAccountCommand command)
    : ResultEndpoint<CreateInvestmentAccountRequest, CreateInvestmentAccountResponse>
{
    public override void Configure()
    {
        Post("investments/accounts");
        Description(b => b.WithName("CreateInvestmentAccount"));
    }

    public override async Task HandleAsync(CreateInvestmentAccountRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}

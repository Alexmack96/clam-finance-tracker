using Clam.Api.Infrastructure.Results;
using FastEndpoints;

namespace Clam.Api.Features.Investments.DeleteInvestmentAccount;

public sealed class DeleteInvestmentAccountEndpoint(DeleteInvestmentAccountCommand command)
    : ResultEndpointWithoutResponse<DeleteInvestmentAccountRequest>
{
    public override void Configure()
    {
        Delete("investments/accounts/{Id}");
        AllowAnonymous();
        Description(b => b.WithName("DeleteInvestmentAccount"));
    }

    public override async Task HandleAsync(DeleteInvestmentAccountRequest req, CancellationToken ct)
        => await SendResultAsync(await command.ExecuteAsync(req, ct), ct);
}

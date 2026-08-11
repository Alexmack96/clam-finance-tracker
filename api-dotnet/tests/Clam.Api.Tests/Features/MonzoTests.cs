using Clam.Api.Infrastructure.Monzo;

namespace Clam.Api.Tests.Features;

public class GetMonzoStatusTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Reports_connected_once_a_credential_exists()
    {
        await Given.MonzoConnectedAsync();
        var response = await Get("/api/admin/monzo/status");
        await Verify(response);
    }

    [Fact]
    public async Task Reports_configured_but_not_connected_before_anyone_links_an_account()
    {
        await Given.NothingAsync();
        var response = await Get("/api/admin/monzo/status");
        await Verify(response);
    }
}

public class SyncMonzoTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Refuses_to_sync_when_no_account_is_connected()
    {
        await Given.NothingAsync();
        var response = await Post("/api/admin/monzo/sync");
        await Verify(response);
    }

    private async Task MonzoHas(params MonzoTransaction[] transactions)
    {
        await Given.MonzoConnectedAsync();
        Api.Monzo.Transactions = [.. transactions];
    }

    [Fact]
    public async Task Stages_settled_transactions_and_records_a_reconciliation()
    {
        await MonzoHas(MonzoFixtures.Settled("tx_coffee", -450, "Pret"));
        var response = await Post("/api/admin/monzo/sync");
        await Verify(response);
    }

    [Fact]
    public async Task Ignores_a_transaction_that_has_not_settled()
    {
        await MonzoHas(MonzoFixtures.Pending("tx_pending", -999, "Authorisation"));
        var response = await Post("/api/admin/monzo/sync");
        await Verify(response);
    }
}

public class MonzoReconciliationTests(ClamApiFactory api) : ApiTest(api)
{
    private async Task MonzoHasATransactionStagingNeverSaw()
    {
        await Given.MonzoConnectedAsync();
        Api.Monzo.Transactions = [MonzoFixtures.Settled("tx_missed", -1200, "Gails")];
    }

    [Fact]
    public async Task Backfills_a_transaction_the_incremental_sync_missed()
    {
        await MonzoHasATransactionStagingNeverSaw();
        var response = await Post("/api/admin/monzo/rec/run");
        await Verify(response);
    }

    [Fact]
    public async Task Lists_nothing_before_any_reconciliation_has_run()
    {
        await Given.NothingAsync();
        var response = await Get("/api/admin/monzo/rec");
        await Verify(response);
    }
}

public class DisconnectMonzoTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Answers_unauthorized_without_a_session_cookie()
    {
        await Given.MonzoConnectedAsync();
        var response = await Post("/api/admin/monzo/disconnect");
        await Verify(response);
    }
}

/// Monzo API payloads, built here rather than in <see cref="Arrange"/> because
/// they are inputs to the stubbed client rather than rows in the database.
internal static class MonzoFixtures
{
    private static readonly DateTime Created = new(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);

    internal static MonzoTransaction Settled(string id, int amountPence, string merchant) => new()
    {
        Id = id,
        Created = Created,
        Settled = "2026-08-01T10:00:00Z",
        Amount = amountPence,
        Currency = "GBP",
        LocalAmount = amountPence,
        LocalCurrency = "GBP",
        Description = merchant.ToUpperInvariant(),
        Category = "eating_out",
        IncludeInSpending = true,
        AccountId = "acc_retail",
        Merchant = new MonzoMerchant { Name = merchant },
    };

    /// No `settled` timestamp — an authorisation that may still vanish, which is
    /// why nothing imports it.
    internal static MonzoTransaction Pending(string id, int amountPence, string merchant)
    {
        var tx = Settled(id, amountPence, merchant);
        tx.Settled = null;
        return tx;
    }
}

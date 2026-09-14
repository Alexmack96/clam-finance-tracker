namespace Clam.Api.Tests.Features;

public class GetFlexRecTests(ClamApiFactory api) : ApiTest(api)
{
    /// Every outcome in one history: a repayment that clears the card, one that
    /// leaves a balance a later repayment clears, a refund that is not judged as
    /// a repayment, and a last repayment that leaves 89.50 owed.
    [Fact]
    public async Task Reports_the_balance_and_whether_each_repayment_cleared_the_card()
    {
        await Given.FlexHistoryAsync();
        var response = await Get("/api/admin/flex/rec");
        await Verify(response);
    }

    [Fact]
    public async Task Reports_a_zero_balance_when_there_are_no_flex_transactions()
    {
        await Given.SeededAsync();
        var response = await Get("/api/admin/flex/rec");
        await Verify(response);
    }
}

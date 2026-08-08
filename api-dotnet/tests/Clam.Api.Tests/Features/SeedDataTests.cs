using System.Net;

namespace Clam.Api.Tests.Features;

/// The only slice on the Result pattern, so this is where the mapping from
/// ResultStatus to HTTP status and RFC 7807 body is actually exercised.
public class SeedDataTests(ClamSeedApp app) : ApiTestBase<ClamSeedApp>(app)
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(20_001)]
    public async Task Rejects_an_out_of_range_count_with_a_validation_problem(int count)
    {
        var response = await PostAsync("/api/dev/seed", new { count });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // FastEndpoints' own Validator<TRequest> produced this, and
        // Errors.UseProblemDetails() gave it the same shape a Result failure has.
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        using var json = await ReadJsonAsync(response);
        json.RootElement.GetProperty("status").GetInt32().ShouldBe(400);
    }

    [Fact]
    public async Task Seeds_the_requested_number_of_transactions()
    {
        var response = await PostAsync("/api/dev/seed", new { count = 25 });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var json = await ReadJsonAsync(response);
        json.RootElement.GetProperty("transactions").GetInt32().ShouldBe(25);
        json.RootElement.GetProperty("categories").GetInt32().ShouldBeGreaterThan(0);
    }
}

/// Shares ClamSeedApp's database. The precondition is set up inside the test
/// rather than in fixture setup, so it cannot leak into any other test — and
/// ApiTestBase re-seeds before each one regardless.
public class SeedDataGuardTests(ClamSeedApp app) : ApiTestBase<ClamSeedApp>(app)
{
    [Fact]
    public async Task Refuses_to_wipe_a_database_holding_a_real_bank_import()
    {
        await TestDataSeeder.InsertRealBankRowAsync(App.ConnectionString, Ct);

        var response = await PostAsync("/api/dev/seed", new { count = 10 });

        // Result.Conflict -> 409, not an exception -> 500. The distinction is the
        // reason the Result pattern is here at all: this is a correct refusal,
        // not a defect.
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        using var json = await ReadJsonAsync(response);
        json.RootElement.GetProperty("title").GetString().ShouldBe("Conflict");
        json.RootElement.GetProperty("detail").GetString()!.ShouldContain("amex:ref_realimport");
        // Every problem response carries the correlation id the logs use.
        json.RootElement.TryGetProperty("traceId", out _).ShouldBeTrue();
    }
}

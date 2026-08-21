namespace Clam.Api.Tests.Features;

public class GetUsersTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Lists_users_newest_first()
    {
        await Given.MonzoConnectedAsync();
        var response = await Get("/api/admin/users");
        await Verify(response);
    }

    [Fact]
    public async Task Lists_nothing_when_there_are_no_users()
    {
        await Given.NothingAsync();
        var response = await Get("/api/admin/users");
        await Verify(response);
    }
}

public class GetCurrentUserTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Answers_unauthorized_without_a_token()
    {
        await Given.NothingAsync();
        var response = await Get("/api/me");
        await Verify(response);
    }
}

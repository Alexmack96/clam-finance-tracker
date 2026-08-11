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

public class CreateUserTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Creates_a_user_without_ever_returning_the_credential()
    {
        await Given.NothingAsync();
        var response = await Post("/api/admin/users",
            new { name = "Casey", email = "casey@example.com", password = "correct-horse" });
        await Verify(response);
    }

    [Fact]
    public async Task Rejects_an_email_that_is_already_registered()
    {
        await Given.MonzoConnectedAsync();
        var response = await Post("/api/admin/users",
            new { name = "Impostor", email = "alex@example.com", password = "correct-horse" });
        await Verify(response);
    }

    [Fact]
    public async Task Rejects_a_password_that_is_too_short()
    {
        await Given.NothingAsync();
        var response = await Post("/api/admin/users",
            new { name = "Casey", email = "casey@example.com", password = "short" });
        await Verify(response);
    }

    [Fact]
    public async Task Rejects_an_address_that_is_not_an_email()
    {
        await Given.NothingAsync();
        var response = await Post("/api/admin/users",
            new { name = "Casey", email = "not-an-email", password = "correct-horse" });
        await Verify(response);
    }
}

public class GetCurrentUserTests(ClamApiFactory api) : ApiTest(api)
{
    [Fact]
    public async Task Answers_unauthorized_without_a_session_cookie()
    {
        await Given.NothingAsync();
        var response = await Get("/api/me");
        await Verify(response);
    }
}

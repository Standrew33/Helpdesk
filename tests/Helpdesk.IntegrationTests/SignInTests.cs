using System.Net;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Pages.Account;

namespace Helpdesk.IntegrationTests;

public class SignInTests(SqlServerFixture sql) : WebTestBase(sql)
{
    [Fact]
    public async Task An_unknown_address_and_a_wrong_password_look_the_same()
    {
        await CreateUserAsync("Customer", "alice@example.test");

        var wrongPassword = await NewClient().SignInAsync("alice@example.test", "wrong-password-1234");
        var unknown = await NewClient().SignInAsync("nobody@example.test", "wrong-password-1234");

        Assert.Equal(wrongPassword.StatusCode, unknown.StatusCode);
        Assert.Contains(LoginModel.FailureMessage, await wrongPassword.Content.ReadAsStringAsync());
        Assert.Contains(LoginModel.FailureMessage, await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_locked_account_gives_the_same_answer_as_a_wrong_password()
    {
        await CreateUserAsync("Customer", "alice@example.test");
        var client = NewClient();

        for (var i = 0; i < 5; i++)
        {
            await client.SignInAsync("alice@example.test", "wrong-password-1234");
        }

        var locked = await client.SignInAsync("alice@example.test", TestPassword);

        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Contains(LoginModel.FailureMessage, await locked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Guessing_the_current_password_on_the_account_page_locks_the_account()
    {
        await CreateUserAsync("Customer", "alice@example.test");
        var session = NewClient();
        await session.SignInAsync("alice@example.test", TestPassword);

        for (var i = 0; i < 5; i++)
        {
            await session.PostFormAsync("/Account/Settings", "/Account/Settings?handler=Password", new Dictionary<string, string>
            {
                ["Password.CurrentPassword"] = "wrong-guess-" + i,
                ["Password.NewPassword"] = "Another-long-phrase-83",
                ["Password.ConfirmPassword"] = "Another-long-phrase-83",
            });
        }

        var later = await NewClient().SignInAsync("alice@example.test", TestPassword);

        Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        Assert.Contains(LoginModel.FailureMessage, await later.Content.ReadAsStringAsync());
    }
}

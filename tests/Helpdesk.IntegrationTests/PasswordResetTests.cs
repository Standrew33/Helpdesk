using System.Net;
using Helpdesk.IntegrationTests.Infrastructure;

namespace Helpdesk.IntegrationTests;

public class PasswordResetTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private const string Email = "alice@example.test";
    private const string NewPassword = "tram7Lamp-ocean";

    private static Task<HttpResponseMessage> RequestResetAsync(AppClient client, string email)
        => client.PostFormAsync("/Account/ForgotPassword", "/Account/ForgotPassword", new Dictionary<string, string> { ["Input.Email"] = email });

    /// <summary>Asks for a reset and enters the code from the e-mail; returns the response to the code.</summary>
    private async Task<HttpResponseMessage> ProveResetAsync(AppClient client, string email)
    {
        var codePage = (await RequestResetAsync(client, email)).Headers.Location!;
        return await client.EnterCodeAsync(codePage, CapturingEmailSender.CodeIn(Factory.Mail.LastTo(email)));
    }

    private static Task<HttpResponseMessage> ChooseAsync(AppClient client, string password)
        => client.PostFormAsync("/Account/NewPassword", "/Account/NewPassword", new Dictionary<string, string>
        {
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
        });

    [Fact]
    public async Task A_reset_code_leads_to_a_new_password_and_signs_in()
    {
        await CreateUserAsync("Customer", Email);
        var client = NewClient();

        var proven = await ProveResetAsync(client, Email);

        Assert.Equal("/Account/NewPassword", proven.Headers.Location!.OriginalString);
        Assert.Contains("password reset", Factory.Mail.LastTo(Email).Subject);

        // The code alone does not sign anyone in; only the new password does.
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Tickets")).StatusCode);

        var saved = await ChooseAsync(client, NewPassword);

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Tickets")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await NewClient().SignInAsync(Email, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await NewClient().SignInAsync(Email, TestPassword)).StatusCode);
    }

    [Fact]
    public async Task The_link_from_the_reset_e_mail_works_too()
    {
        await CreateUserAsync("Customer", Email);
        await RequestResetAsync(NewClient(), Email);
        var link = new Uri(CapturingEmailSender.LinkIn(Factory.Mail.LastTo(Email)));
        var client = NewClient();

        Assert.Contains("Confirm the password reset", await client.GetHtmlAsync(link.PathAndQuery));

        var response = await client.PostFormAsync(link.PathAndQuery, link.PathAndQuery, new Dictionary<string, string>
        {
            ["Token"] = System.Web.HttpUtility.ParseQueryString(link.Query)["token"]!,
        });

        Assert.Equal("/Account/NewPassword", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Resetting_signs_out_every_other_session()
    {
        await CreateUserAsync("Customer", Email);
        var elsewhere = NewClient();
        await elsewhere.SignInAsync(Email, TestPassword);
        var client = NewClient();

        await ProveResetAsync(client, Email);
        await ChooseAsync(client, NewPassword);

        var stale = await elsewhere.GetAsync("/Tickets");
        Assert.Equal(HttpStatusCode.Redirect, stale.StatusCode);
        Assert.Contains("/Account/Login", stale.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Resetting_lifts_a_lock_out()
    {
        await CreateUserAsync("Customer", Email);

        for (var i = 0; i < 5; i++)
        {
            await NewClient().SignInAsync(Email, "wrong-password-1234");
        }

        var client = NewClient();
        await ProveResetAsync(client, Email);
        await ChooseAsync(client, NewPassword);

        Assert.Equal(HttpStatusCode.Redirect, (await NewClient().SignInAsync(Email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task A_weak_new_password_is_refused()
    {
        await CreateUserAsync("Customer", Email);
        var client = NewClient();
        await ProveResetAsync(client, Email);

        var response = await ChooseAsync(client, "password123456");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await NewClient().SignInAsync(Email, TestPassword)).StatusCode);
    }

    [Fact]
    public async Task The_new_password_step_needs_a_proven_reset()
    {
        await CreateUserAsync("Customer", Email);
        var client = NewClient();

        Assert.Contains("/Account/ForgotPassword", (await client.GetAsync("/Account/NewPassword")).Headers.Location!.OriginalString);

        var response = await client.PostFormAsync("/Account/ForgotPassword", "/Account/NewPassword", new Dictionary<string, string>
        {
            ["Input.Password"] = NewPassword,
            ["Input.ConfirmPassword"] = NewPassword,
        });

        Assert.Contains("/Account/ForgotPassword", response.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await NewClient().SignInAsync(Email, TestPassword)).StatusCode);
    }

    [Fact]
    public async Task Known_and_unknown_addresses_get_the_same_answer()
    {
        await CreateUserAsync("Customer", Email);

        var known = await RequestResetAsync(NewClient(), Email);
        var unknown = await RequestResetAsync(NewClient(), "stranger@example.test");

        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.StartsWith("/Account/Code?c=", unknown.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reset_for_an_address_without_an_account_leads_to_sign_up()
    {
        var response = await ProveResetAsync(NewClient(), "stranger@example.test");

        Assert.Equal("/Account/Welcome", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_sign_in_code_cannot_be_used_to_reset_and_the_other_way_round()
    {
        await CreateUserAsync("Customer", Email);
        var client = NewClient();

        var response = await SignInWithCodeAsync(client, Email);

        Assert.Contains("/Tickets", response.Headers.Location!.OriginalString);
        Assert.Contains("/Account/ForgotPassword", (await client.GetAsync("/Account/NewPassword")).Headers.Location!.OriginalString);
    }
}

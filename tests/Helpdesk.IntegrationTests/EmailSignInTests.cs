using System.Net;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

public class EmailSignInTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private const string Email = "alice@example.test";

    private WebApplicationFactory<Program> WithSettings(params (string Key, string Value)[] settings)
        => Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))));

    private async Task<ApplicationUser> UserAsync(string email)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!;
    }

    [Fact]
    public async Task Entering_an_address_sends_a_code_and_a_link_and_shows_the_code_page()
    {
        var client = NewClient();

        var response = await client.RequestCodeAsync(Email);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Code?c=", response.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var mail = Factory.Mail.LastTo(Email);
        Assert.Matches("^[0-9]{6}$", CapturingEmailSender.CodeIn(mail));
        Assert.Contains("/Account/Link?token=", CapturingEmailSender.LinkIn(mail));
        Assert.Contains(CapturingEmailSender.CodeIn(mail), mail.Subject);

        var page = await client.GetHtmlAsync(response.Headers.Location.OriginalString);
        Assert.Contains("Check your e-mail", page);
        Assert.DoesNotContain(Email, page);
    }

    [Fact]
    public async Task Known_and_unknown_addresses_get_the_same_answer()
    {
        await CreateUserAsync("Customer", Email);

        var known = await NewClient().RequestCodeAsync(Email);
        var unknown = await NewClient().RequestCodeAsync("stranger@example.test");

        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.StartsWith("/Account/Code?c=", unknown.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Single(Factory.Mail.To("stranger@example.test"));
    }

    [Fact]
    public async Task The_code_signs_an_existing_user_in_and_confirms_the_address()
    {
        await CreateUserAsync("Customer", Email);
        var client = NewClient();

        var response = await SignInWithCodeAsync(client, Email);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Tickets", response.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Tickets")).StatusCode);
        Assert.True((await UserAsync(Email)).EmailConfirmed);
    }

    [Fact]
    public async Task The_return_address_is_kept_through_the_steps()
    {
        await CreateUserAsync("Customer", Email);

        var response = await SignInWithCodeAsync(NewClient(), Email, returnUrl: "/Account/Settings");

        Assert.Equal("/Account/Settings", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_return_address_on_another_site_is_ignored()
    {
        await CreateUserAsync("Customer", Email);

        var response = await SignInWithCodeAsync(NewClient(), Email, returnUrl: "https://evil.example/");

        Assert.DoesNotContain("evil.example", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_wrong_code_tells_how_many_tries_are_left_and_the_fifth_ends_the_code()
    {
        await CreateUserAsync("Customer", Email);
        var client = NewClient();
        var codePage = (await client.RequestCodeAsync(Email)).Headers.Location!;
        var right = CapturingEmailSender.CodeIn(Factory.Mail.LastTo(Email));
        var wrong = right == "000000" ? "111111" : "000000";

        var first = await client.EnterCodeAsync(codePage, wrong);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("4 attempts left", await first.Content.ReadAsStringAsync());

        for (var i = 0; i < 4; i++)
        {
            await client.EnterCodeAsync(codePage, wrong);
        }

        // The page no longer offers a form; a hand-made post with the right code is refused too.
        Assert.Contains("This code no longer works", await client.GetHtmlAsync(codePage.OriginalString));
        var afterwards = await client.PostFormAsync("/Account/Login", codePage.OriginalString, new Dictionary<string, string> { ["Code"] = right });
        Assert.Contains("This code no longer works", await afterwards.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Tickets")).StatusCode);
    }

    [Fact]
    public async Task Opening_the_link_only_asks_for_a_click()
    {
        await CreateUserAsync("Customer", Email);
        await NewClient().RequestCodeAsync(Email);
        var link = new Uri(CapturingEmailSender.LinkIn(Factory.Mail.LastTo(Email)));
        var client = NewClient();

        // A mail scanner opening the link must neither sign in nor use the link up.
        var preview = await client.GetHtmlAsync(link.PathAndQuery);
        Assert.Contains("Continue as", preview);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Tickets")).StatusCode);

        var response = await client.PostFormAsync(link.PathAndQuery, link.PathAndQuery, new Dictionary<string, string>
        {
            ["Token"] = System.Web.HttpUtility.ParseQueryString(link.Query)["token"]!,
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Tickets")).StatusCode);
    }

    [Fact]
    public async Task The_link_works_once()
    {
        await CreateUserAsync("Customer", Email);
        await NewClient().RequestCodeAsync(Email);
        var link = new Uri(CapturingEmailSender.LinkIn(Factory.Mail.LastTo(Email)));
        var token = System.Web.HttpUtility.ParseQueryString(link.Query)["token"]!;

        await NewClient().PostFormAsync(link.PathAndQuery, link.PathAndQuery, new Dictionary<string, string> { ["Token"] = token });
        var again = NewClient();
        var page = await again.GetHtmlAsync(link.PathAndQuery);

        Assert.Contains("This link no longer works", page);
    }

    [Fact]
    public async Task A_deactivated_account_cannot_sign_in_with_a_code()
    {
        await CreateUserAsync("Customer", Email, isActive: false);
        var client = NewClient();

        var response = await SignInWithCodeAsync(client, Email);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("deactivated", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Tickets")).StatusCode);
    }

    [Fact]
    public async Task A_new_code_can_be_sent_and_replaces_the_old_one()
    {
        using var factory = WithSettings(("OneTimeCodes:ResendAfter", "00:00:00"));
        var client = NewClient(factory);
        var codePage = (await client.RequestCodeAsync(Email)).Headers.Location!;
        var oldCode = CapturingEmailSender.CodeIn(Factory.Mail.LastTo(Email));

        var resent = await client.PostFormAsync(codePage.OriginalString, codePage.OriginalString.Replace("/Account/Code?", "/Account/Code?handler=Resend&", StringComparison.Ordinal), new Dictionary<string, string>());
        var newPage = resent.Headers.Location!;

        Assert.Equal(2, Factory.Mail.To(Email).Count);
        Assert.Contains("We sent a new code", await client.GetHtmlAsync(newPage.OriginalString));
        var old = await client.PostFormAsync("/Account/Login", codePage.OriginalString, new Dictionary<string, string> { ["Code"] = oldCode });
        Assert.Contains("This code no longer works", await old.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Asking_again_within_a_minute_sends_nothing_new()
    {
        var client = NewClient();
        var first = (await client.RequestCodeAsync(Email)).Headers.Location!;

        var second = await client.RequestCodeAsync(Email);

        Assert.Single(Factory.Mail.To(Email));
        Assert.Equal(first, second.Headers.Location);
        Assert.Contains("a moment ago", await client.GetHtmlAsync(second.Headers.Location!.OriginalString));
    }

    [Fact]
    public async Task Links_use_the_configured_address_and_never_the_requests_host()
    {
        using var factory = WithSettings(("Site:PublicUrl", "https://helpdesk.example.test"));
        var client = NewClient(factory);
        client.AddHeader("X-Forwarded-Host", "evil.example");

        await client.PostFormAsync("/Account/Login", "/Account/Login", new Dictionary<string, string> { ["Input.Email"] = Email });

        Assert.StartsWith("https://helpdesk.example.test/Account/Link?token=", CapturingEmailSender.LinkIn(Factory.Mail.LastTo(Email)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_invalid_address_sends_nothing()
    {
        var response = await NewClient().RequestCodeAsync("not-an-address");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Factory.Mail.Messages);
    }
}

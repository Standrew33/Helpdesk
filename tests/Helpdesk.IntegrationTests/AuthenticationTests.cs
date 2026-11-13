using System.Net;
using System.Text.RegularExpressions;
using Helpdesk.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace Helpdesk.IntegrationTests;

public class AuthenticationTests(SqlServerFixture sql) : WebTestBase(sql)
{
    [Fact]
    public async Task Anonymous_user_is_redirected_to_the_login_page()
    {
        var client = NewClient();

        var response = await client.GetAsync("/Tickets");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Home_page_sends_anonymous_visitors_to_the_sign_in_page()
    {
        var response = await NewClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Sign_in_page_is_public()
    {
        var response = await NewClient().GetAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Search_console_verification_tag_is_written_when_configured()
    {
        using var factory = Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Site:GoogleSiteVerification"] = "token-123" })));

        var html = await NewClient(factory).GetHtmlAsync("/Account/Login");

        Assert.Contains("<meta name=\"google-site-verification\" content=\"token-123\"", html);
    }

    [Fact]
    public async Task Search_console_verification_tag_is_absent_by_default()
    {
        var html = await NewClient().GetHtmlAsync("/Account/Login");

        Assert.DoesNotContain("google-site-verification", html);
    }

    [Theory]
    [InlineData("/favicon.svg", "image/svg+xml")]
    [InlineData("/favicon.ico", "image/x-icon")]
    [InlineData("/apple-touch-icon.png", "image/png")]
    public async Task Icons_are_public_and_linked_from_the_pages(string path, string contentType)
    {
        var client = NewClient();

        var response = await client.GetAsync(path);
        var html = await client.GetHtmlAsync("/Account/Login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);

        // Static files are linked with a content fingerprint in the name, e.g. /favicon.abc123.svg.
        var stem = Regex.Escape(Path.GetFileNameWithoutExtension(path));
        var extension = Regex.Escape(Path.GetExtension(path));
        Assert.Matches($"href=\"/{stem}(\\.[a-z0-9]+)?{extension}\"", html);
    }

    [Fact]
    public async Task Valid_credentials_sign_the_user_in()
    {
        await CreateUserAsync("Customer", "alice@example.test");
        var client = NewClient();

        var response = await client.SignInAsync("alice@example.test", TestPassword);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Tickets")).StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        await CreateUserAsync("Customer", "alice@example.test");
        var client = NewClient();

        var response = await client.SignInAsync("alice@example.test", "wrong-password");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid e-mail or password", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Tickets")).StatusCode);
    }

    [Fact]
    public async Task Inactive_user_cannot_sign_in()
    {
        await CreateUserAsync("Customer", "gone@example.test", isActive: false);
        var client = NewClient();

        var response = await client.SignInAsync("gone@example.test", TestPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid e-mail or password", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Account_is_locked_after_repeated_failures()
    {
        await CreateUserAsync("Customer", "alice@example.test");
        var client = NewClient();

        for (var i = 0; i < 5; i++)
        {
            await client.SignInAsync("alice@example.test", "wrong-password");
        }

        var response = await client.SignInAsync("alice@example.test", TestPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("temporarily locked", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Demo_sign_in_works_when_enabled()
    {
        var client = await SignedInAsync("Operator");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Tickets")).StatusCode);
    }

    [Fact]
    public async Task Demo_sign_in_is_unavailable_when_disabled()
    {
        var factory = await CreateFactoryWithoutDemoAsync();
        var client = NewClient(factory);

        var response = await client.PostFormAsync("/Account/Login", "/Account/Login?handler=Demo&role=Admin", new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Demo: sign in", await client.GetHtmlAsync("/Account/Login"));
    }

    [Fact]
    public async Task Unknown_addresses_show_a_friendly_not_found_page()
    {
        var client = await SignedInAsync("Customer");

        var response = await client.GetAsync("/Tickets/Details/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Refused_posts_to_missing_tickets_also_show_the_friendly_page()
    {
        var client = await SignedInAsync("Customer");

        var response = await client.PostFormAsync("/Tickets", "/Tickets/Details/999999?handler=Comment", new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_cannot_open_the_page_for_creating_tickets()
    {
        var admin = await SignedInAsync("Admin");

        var response = await admin.GetAsync("/Tickets/Create");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.OriginalString);
    }
}

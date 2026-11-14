using System.Net;
using Helpdesk.IntegrationTests.Infrastructure;

namespace Helpdesk.IntegrationTests;

public class SessionTests(SqlServerFixture sql) : WebTestBase(sql)
{
    [Fact]
    public async Task Cookies_are_secure_http_only_and_bound_to_the_host()
    {
        var client = NewClient();

        var form = await client.GetAsync("/Account/Login");
        var antiforgery = form.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-Helpdesk.Antiforgery", StringComparison.Ordinal));

        Assert.Contains("secure", antiforgery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", antiforgery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", antiforgery, StringComparison.OrdinalIgnoreCase);

        var demo = await client.PostFormAsync("/Account/Login", "/Account/Login?handler=Demo&role=Customer", new Dictionary<string, string>());
        var auth = demo.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-Helpdesk.Auth", StringComparison.Ordinal));

        Assert.Contains("secure", auth, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", auth, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", auth, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", auth, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", auth, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Signing_out_needs_a_post_with_a_valid_token()
    {
        var client = await SignedInAsync("Customer");

        var get = await client.GetAsync("/Account/Logout");
        Assert.Equal(HttpStatusCode.Redirect, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Tickets")).StatusCode);

        var withoutToken = await client.PostWithoutTokenAsync("/Account/Logout");
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Tickets")).StatusCode);
    }

    [Fact]
    public async Task A_post_without_an_anti_forgery_token_is_refused()
    {
        var client = await SignedInAsync("Customer");

        var response = await client.PostWithoutTokenAsync("/Tickets/Create", new Dictionary<string, string>
        {
            ["Input.Subject"] = "Forged",
            ["Input.Description"] = "Sent from another site.",
            ["Input.Priority"] = "Normal",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unsafe_return_addresses_are_not_followed_after_sign_in()
    {
        await CreateUserAsync("Customer", "alice@example.test");

        var response = await NewClient().PostFormAsync(
            "/Account/Password", "/Account/Password?returnUrl=https%3A%2F%2Fevil.example%2F",
            new Dictionary<string, string> { ["Input.Email"] = "alice@example.test", ["Input.Password"] = TestPassword });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.example", response.Headers.Location.OriginalString);
    }
}

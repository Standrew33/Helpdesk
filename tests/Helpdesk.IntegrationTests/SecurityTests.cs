using System.Net;
using System.Text.RegularExpressions;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

public partial class SecurityTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private const char Rlo = (char)0x202E;
    private const char ZeroWidthSpace = (char)0x200B;
    private WebApplicationFactory<Program> WithSettings(params (string Key, string Value)[] settings)
        => Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))));

    private async Task<int> RealTicketAsync(string subject, string email = "real.customer@example.test")
    {
        await CreateUserAsync("Customer", email);
        var customer = NewClient();
        await customer.SignInAsync(email, TestPassword);
        return await customer.CreateTicketAsync(subject);
    }

    private async Task<IList<string>> RolesOfAsync(string userId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GetRolesAsync((await users.FindByIdAsync(userId))!);
    }

    // ---- Response headers and cookies ------------------------------------------------------------------------------

    [Fact]
    public async Task Pages_carry_the_security_headers()
    {
        var response = await NewClient().GetAsync("/Account/Login");

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("camera=()", response.Headers.GetValues("Permissions-Policy").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Pages_contain_no_inline_script_or_style_that_the_policy_would_block()
    {
        await CreateUserAsync("Customer", "page.checker@example.test");
        var real = NewClient();
        await real.SignInAsync("page.checker@example.test", TestPassword);
        var ticketId = await real.CreateTicketAsync("A ticket to look at");
        await CreateUserAsync("Operator", "page.agent@example.test");
        var agent = NewClient();
        await agent.SignInAsync("page.agent@example.test", TestPassword);

        var pages = new List<(AppClient Client, string Url)>
        {
            (NewClient(), "/Account/Login"),
            (NewClient(), "/Account/Register"),
            (real, "/Tickets"),
            (real, "/Tickets/Create"),
            (real, $"/Tickets/Details/{ticketId}"),
            (real, "/Account/Settings"),
            (real, "/Account/TwoFactor"),
            (agent, $"/Tickets/Details/{ticketId}"),
            (await SignedInAsync("Admin"), "/Admin/Users"),
        };

        foreach (var (client, url) in pages)
        {
            var html = await client.GetHtmlAsync(url);

            Assert.DoesNotMatch(InlineScript(), html);
            Assert.DoesNotMatch(InlineStyleAttribute(), html);
            Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(InlineHandler(), html);
        }
    }

    [Fact]
    public async Task Static_files_keep_their_own_caching()
    {
        var client = NewClient();
        var html = await client.GetHtmlAsync("/Account/Login");
        var stylesheet = Regex.Match(html, "href=\"(/css/site[^\"]*\\.css)\"").Groups[1].Value;

        var response = await client.GetAsync(stylesheet);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

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

    // ---- Request limits --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Too_many_sign_in_attempts_from_one_address_are_stopped()
    {
        using var factory = WithSettings(("RequestLimits:SignInAttemptsPerMinute", "3"));
        var client = NewClient(factory);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.SignInAsync("nobody@example.test", "wrong-password-1234")).StatusCode);
        }

        var blocked = await client.SignInAsync("nobody@example.test", "wrong-password-1234");

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));
        Assert.Contains("Too many attempts", await blocked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reading_pages_is_never_limited()
    {
        using var factory = WithSettings(("RequestLimits:SignInAttemptsPerMinute", "1"));
        var client = NewClient(factory);

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Login")).StatusCode);
        }
    }

    [Fact]
    public async Task Each_address_has_its_own_allowance()
    {
        using var factory = WithSettings(("RequestLimits:SignInAttemptsPerMinute", "2"));
        var first = NewClient(factory);
        first.AddHeader("X-Forwarded-For", "203.0.113.10");
        var second = NewClient(factory);
        second.AddHeader("X-Forwarded-For", "203.0.113.20");

        await first.SignInAsync("nobody@example.test", "wrong-password-1234");
        await first.SignInAsync("nobody@example.test", "wrong-password-1234");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.SignInAsync("nobody@example.test", "wrong-password-1234")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.SignInAsync("nobody@example.test", "wrong-password-1234")).StatusCode);
    }

    [Fact]
    public async Task Registrations_from_one_address_are_limited()
    {
        using var factory = WithSettings(("RequestLimits:RegistrationsPerHour", "2"));

        // Each registration comes from a fresh browser, but from the same address.
        for (var i = 0; i < 2; i++)
        {
            var response = await NewClient(factory).PostFormAsync("/Account/Register", "/Account/Register", RegistrationForm($"person{i}@example.test"));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        var blocked = await NewClient(factory).PostFormAsync("/Account/Register", "/Account/Register", RegistrationForm("person9@example.test"));

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task Uploads_are_limited_per_address()
    {
        using var factory = WithSettings(("RequestLimits:UploadsPerHour", "1"));
        var customer = NewClient(factory);
        await customer.SignInAsDemoAsync("Customer");
        var id = await customer.CreateTicketAsync("Many files");

        await customer.UploadAttachmentAsync(id, "one.txt", "first"u8.ToArray());
        var second = await customer.UploadAttachmentAsync(id, "two.txt", "second"u8.ToArray());

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    // ---- Sign-in and registration ----------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_address_and_a_wrong_password_look_the_same()
    {
        await CreateUserAsync("Customer", "alice@example.test");

        var wrongPassword = await NewClient().SignInAsync("alice@example.test", "wrong-password-1234");
        var unknown = await NewClient().SignInAsync("nobody@example.test", "wrong-password-1234");

        Assert.Equal(wrongPassword.StatusCode, unknown.StatusCode);
        var first = await wrongPassword.Content.ReadAsStringAsync();
        var second = await unknown.Content.ReadAsStringAsync();
        Assert.Contains(Web.Pages.Account.LoginModel.FailureMessage, first);
        Assert.Contains(Web.Pages.Account.LoginModel.FailureMessage, second);
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
        Assert.Contains(Web.Pages.Account.LoginModel.FailureMessage, await locked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_registration_spam_trap_creates_no_account()
    {
        var client = NewClient();
        var form = RegistrationForm("bot@example.test");
        form["Input.Website"] = "https://spam.example";

        var response = await client.PostFormAsync("/Account/Register", "/Account/Register", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString);
        await using var scope = Factory.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync("bot@example.test"));
    }

    public static TheoryData<string> UnsafeNames => new()
    {
        "Eve" + Rlo + "Evil",
        "Zero" + ZeroWidthSpace + "Width",
        "Bell" + (char)7,
    };

    [Theory]
    [MemberData(nameof(UnsafeNames))]
    public async Task Names_with_invisible_or_reordering_characters_are_refused(string name)
    {
        var form = RegistrationForm("eve@example.test");
        form["Input.DisplayName"] = name;

        var response = await NewClient().PostFormAsync("/Account/Register", "/Account/Register", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("contains characters that are not allowed", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_demo_address_with_stray_spaces_is_still_reserved()
    {
        var form = RegistrationForm(" admin@helpdesk.test ");

        var response = await NewClient().PostFormAsync("/Account/Register", "/Account/Register", form);

        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Unsafe_return_addresses_are_not_followed_after_sign_in()
    {
        await CreateUserAsync("Customer", "alice@example.test");
        var client = NewClient();

        var response = await client.PostFormAsync(
            "/Account/Login", "/Account/Login?returnUrl=https%3A%2F%2Fevil.example%2F",
            new Dictionary<string, string> { ["Input.Email"] = "alice@example.test", ["Input.Password"] = TestPassword });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.example", response.Headers.Location.OriginalString);
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

    // ---- The shared demo accounts ----------------------------------------------------------------------------------

    [Fact]
    public async Task Demo_staff_do_not_see_tickets_of_real_visitors()
    {
        var realId = await RealTicketAsync("Private matter of a real visitor");

        foreach (var role in new[] { "Operator", "Admin" })
        {
            var demo = await SignedInAsync(role);

            Assert.DoesNotContain("Private matter", await demo.GetHtmlAsync("/Tickets"));
            Assert.Equal(HttpStatusCode.NotFound, (await demo.GetAsync($"/Tickets/Details/{realId}")).StatusCode);
        }
    }

    [Fact]
    public async Task Demo_staff_cannot_change_tickets_of_real_visitors()
    {
        var realId = await RealTicketAsync("Private matter of a real visitor");
        var demo = await SignedInAsync("Operator");

        var response = await demo.PostFormAsync("/Tickets", $"/Tickets/Details/{realId}?handler=Comment", new Dictionary<string, string>
        {
            ["rowVersion"] = "AAAAAAAAB9E=",
            ["body"] = "I should not be able to write here.",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Real_staff_see_every_ticket()
    {
        var realId = await RealTicketAsync("Private matter of a real visitor");
        await CreateUserAsync("Operator", "agent@example.test");
        var op = NewClient();
        await op.SignInAsync("agent@example.test", TestPassword);

        Assert.Contains("Private matter", await op.GetHtmlAsync("/Tickets"));
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync($"/Tickets/Details/{realId}")).StatusCode);
    }

    [Fact]
    public async Task The_demo_administrator_sees_demo_accounts_only()
    {
        await CreateUserAsync("Customer", "real.person@example.test");
        var demo = await SignedInAsync("Admin");

        var html = await demo.GetHtmlAsync("/Admin/Users");

        Assert.DoesNotContain("real.person@example.test", html);
        Assert.Contains("customer@helpdesk.test", html);
        Assert.DoesNotContain("real.person@example.test", await demo.GetHtmlAsync("/Admin/Users?q=real"));
    }

    [Fact]
    public async Task The_demo_administrator_cannot_hand_out_roles_or_lock_accounts()
    {
        var victimId = await CreateUserAsync("Customer", "victim@example.test");
        var demo = await SignedInAsync("Admin");

        var role = await demo.PostFormAsync("/Tickets", "/Admin/Users?handler=Role", new Dictionary<string, string> { ["id"] = victimId, ["role"] = "Admin" });
        var shown = await (await demo.FollowAsync(role)).Content.ReadAsStringAsync();
        var active = await demo.PostFormAsync("/Tickets", "/Admin/Users?handler=Active", new Dictionary<string, string> { ["id"] = victimId, ["active"] = "false" });

        Assert.Contains("cannot change them", shown);
        Assert.Contains("cannot change them", await (await demo.FollowAsync(active)).Content.ReadAsStringAsync());
        Assert.Equal(["Customer"], await RolesOfAsync(victimId));
        var victim = NewClient();
        Assert.Equal(HttpStatusCode.Redirect, (await victim.SignInAsync("victim@example.test", TestPassword)).StatusCode);
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
        Assert.Contains(Web.Pages.Account.LoginModel.FailureMessage, await later.Content.ReadAsStringAsync());
    }

    private static Dictionary<string, string> RegistrationForm(string email) => new()
    {
        ["Input.DisplayName"] = "Pat Example",
        ["Input.Email"] = email,
        ["Input.Password"] = "Brand-new-pass-42",
        ["Input.ConfirmPassword"] = "Brand-new-pass-42",
    };

    [GeneratedRegex("<script(?![^>]*\\bsrc=)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScript();

    [GeneratedRegex("<[a-z][^>]*\\sstyle=", RegexOptions.IgnoreCase)]
    private static partial Regex InlineStyleAttribute();

    [GeneratedRegex("<[a-z][^>]*\\son[a-z]+=", RegexOptions.IgnoreCase)]
    private static partial Regex InlineHandler();
}

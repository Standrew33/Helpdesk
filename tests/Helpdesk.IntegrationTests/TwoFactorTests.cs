using System.Net;
using System.Text.RegularExpressions;
using Helpdesk.IntegrationTests.Infrastructure;

namespace Helpdesk.IntegrationTests;

public partial class TwoFactorTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private const string Email = "dana@example.test";

    /// <summary>Signs in, turns two-factor authentication on the way a user would, and returns the key and the recovery codes.</summary>
    private async Task<(string Key, List<string> RecoveryCodes)> EnableAsync()
    {
        await CreateUserAsync("Customer", Email);
        var client = NewClient();
        await client.SignInAsync(Email, TestPassword);

        var page = await client.GetHtmlAsync("/Account/TwoFactor");
        var key = KeyPattern().Match(page).Groups[1].Value.Replace(" ", string.Empty, StringComparison.Ordinal);

        var response = await client.PostFormAsync("/Account/TwoFactor", "/Account/TwoFactor?handler=Enable", new Dictionary<string, string>
        {
            ["Verify.Code"] = Totp.CodeFor(key),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var codes = RecoveryCodePattern().Matches(await response.Content.ReadAsStringAsync()).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(10, codes.Count);
        return (key, codes);
    }

    private async Task<(AppClient Client, HttpResponseMessage Response)> PasswordStepAsync()
    {
        var client = NewClient();
        return (client, await client.SignInAsync(Email, TestPassword));
    }

    [Fact]
    public async Task The_setup_page_shows_a_key_and_a_picture_to_scan()
    {
        await CreateUserAsync("Customer", Email);
        var client = NewClient();
        await client.SignInAsync(Email, TestPassword);

        var page = await client.GetHtmlAsync("/Account/TwoFactor");

        Assert.Matches(KeyPattern(), page);
        Assert.Contains("data:image/png;base64,", page);
    }

    [Fact]
    public async Task Turning_it_on_signs_out_the_other_sessions()
    {
        await CreateUserAsync("Customer", Email);
        var other = NewClient();
        await other.SignInAsync(Email, TestPassword);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/Tickets")).StatusCode);

        var current = NewClient();
        await current.SignInAsync(Email, TestPassword);
        var key = KeyPattern().Match(await current.GetHtmlAsync("/Account/TwoFactor")).Groups[1].Value.Replace(" ", string.Empty, StringComparison.Ordinal);
        await current.PostFormAsync("/Account/TwoFactor", "/Account/TwoFactor?handler=Enable", new Dictionary<string, string> { ["Verify.Code"] = Totp.CodeFor(key) });

        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync("/Tickets")).StatusCode);
        var stale = await other.GetAsync("/Tickets");
        Assert.Equal(HttpStatusCode.Redirect, stale.StatusCode);
        Assert.Contains("/Account/Login", stale.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_wrong_code_does_not_turn_two_factor_on()
    {
        await CreateUserAsync("Customer", Email);
        var client = NewClient();
        await client.SignInAsync(Email, TestPassword);

        var response = await client.PostFormAsync("/Account/TwoFactor", "/Account/TwoFactor?handler=Enable", new Dictionary<string, string> { ["Verify.Code"] = "000000" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("code is not correct", await response.Content.ReadAsStringAsync());

        var (_, next) = await PasswordStepAsync();
        Assert.Contains("/Tickets", next.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Once_on_the_password_alone_no_longer_signs_in()
    {
        await EnableAsync();

        var (client, response) = await PasswordStepAsync();

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("LoginWith2fa", response.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Tickets")).StatusCode);
        Assert.Contains("/Account/Login", (await client.GetAsync("/Tickets")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_code_from_the_app_completes_the_sign_in()
    {
        var (key, _) = await EnableAsync();
        var (client, _) = await PasswordStepAsync();

        var response = await client.PostFormAsync("/Account/LoginWith2fa", "/Account/LoginWith2fa", new Dictionary<string, string> { ["Input.Code"] = Totp.CodeFor(key) });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Tickets")).StatusCode);
    }

    [Fact]
    public async Task A_wrong_code_is_refused()
    {
        await EnableAsync();
        var (client, _) = await PasswordStepAsync();

        var response = await client.PostFormAsync("/Account/LoginWith2fa", "/Account/LoginWith2fa", new Dictionary<string, string> { ["Input.Code"] = "123456" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("code is not correct", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Tickets")).StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_codes_lock_the_account()
    {
        var (key, _) = await EnableAsync();
        var (client, _) = await PasswordStepAsync();

        for (var i = 0; i < 5; i++)
        {
            await client.PostFormAsync("/Account/LoginWith2fa", "/Account/LoginWith2fa", new Dictionary<string, string> { ["Input.Code"] = "123456" });
        }

        var response = await client.PostFormAsync("/Account/LoginWith2fa", "/Account/LoginWith2fa", new Dictionary<string, string> { ["Input.Code"] = Totp.CodeFor(key) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("temporarily locked", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_second_step_page_needs_the_password_step_first()
    {
        var response = await NewClient().GetAsync("/Account/LoginWith2fa");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once()
    {
        var (_, codes) = await EnableAsync();

        var (first, _) = await PasswordStepAsync();
        var used = await first.PostFormAsync("/Account/LoginWithRecoveryCode", "/Account/LoginWithRecoveryCode", new Dictionary<string, string> { ["Input.RecoveryCode"] = codes[0] });

        Assert.Equal(HttpStatusCode.Redirect, used.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/Tickets")).StatusCode);

        var (second, _) = await PasswordStepAsync();
        var again = await second.PostFormAsync("/Account/LoginWithRecoveryCode", "/Account/LoginWithRecoveryCode", new Dictionary<string, string> { ["Input.RecoveryCode"] = codes[0] });

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Contains("not correct or was already used", await again.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Turning_it_off_needs_the_password()
    {
        var (key, _) = await EnableAsync();
        var (client, _) = await PasswordStepAsync();
        await client.PostFormAsync("/Account/LoginWith2fa", "/Account/LoginWith2fa", new Dictionary<string, string> { ["Input.Code"] = Totp.CodeFor(key) });

        var wrong = await client.PostFormAsync("/Account/TwoFactor", "/Account/TwoFactor?handler=Disable", new Dictionary<string, string> { ["Confirm.Password"] = "not-my-password-1" });
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("password is not correct", await wrong.Content.ReadAsStringAsync());
        Assert.Contains("LoginWith2fa", (await PasswordStepAsync()).Response.Headers.Location!.OriginalString);

        var right = await client.PostFormAsync("/Account/TwoFactor", "/Account/TwoFactor?handler=Disable", new Dictionary<string, string> { ["Confirm.Password"] = TestPassword });
        Assert.Equal(HttpStatusCode.Redirect, right.StatusCode);
        Assert.Contains("/Tickets", (await PasswordStepAsync()).Response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task New_recovery_codes_replace_the_old_ones()
    {
        var (_, oldCodes) = await EnableAsync();
        var (client, _) = await PasswordStepAsync();

        // The password step opened the second-step page only; a recovery code gets the session in.
        await client.PostFormAsync("/Account/LoginWithRecoveryCode", "/Account/LoginWithRecoveryCode", new Dictionary<string, string> { ["Input.RecoveryCode"] = oldCodes[0] });

        var response = await client.PostFormAsync("/Account/TwoFactor", "/Account/TwoFactor?handler=NewCodes", new Dictionary<string, string> { ["Confirm.Password"] = TestPassword });
        var newCodes = RecoveryCodePattern().Matches(await response.Content.ReadAsStringAsync()).Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(10, newCodes.Count);
        Assert.Empty(newCodes.Intersect(oldCodes));

        var (other, _) = await PasswordStepAsync();
        var stale = await other.PostFormAsync("/Account/LoginWithRecoveryCode", "/Account/LoginWithRecoveryCode", new Dictionary<string, string> { ["Input.RecoveryCode"] = oldCodes[1] });
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
    }

    [Fact]
    public async Task The_shared_demo_accounts_cannot_use_a_second_step()
    {
        var client = await SignedInAsync("Customer");

        var page = await client.GetHtmlAsync("/Account/TwoFactor");
        var post = await client.PostFormAsync("/Account/TwoFactor", "/Account/TwoFactor?handler=Enable", new Dictionary<string, string> { ["Verify.Code"] = "123456" });

        Assert.Contains("shared demo account", page);
        Assert.NotEqual(HttpStatusCode.OK, post.StatusCode);
    }

    [GeneratedRegex("Key:</span> <code>([^<]+)</code>")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("<li><code>([^<]+)</code></li>")]
    private static partial Regex RecoveryCodePattern();
}

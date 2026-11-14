using System.Net;
using System.Text;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Helpdesk.IntegrationTests;

public class CaptchaTests(SqlServerFixture sql) : WebTestBase(sql)
{
    /// <summary>Stands in for Google: the token "person" passes, anything else does not.</summary>
    private sealed class FakeVerifier : ICaptchaVerifier
    {
        public List<string> Actions { get; } = [];

        public Task<CaptchaVerdict> VerifyAsync(string token, string expectedAction, string? remoteIp, CancellationToken ct = default)
        {
            Actions.Add(expectedAction);
            return Task.FromResult(token == "person" ? new CaptchaVerdict(true, 0.9, "ok") : new CaptchaVerdict(false, 0.1, "score too low"));
        }
    }

    private readonly FakeVerifier _verifier = new();

    private WebApplicationFactory<Program> WithCaptcha()
        => Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Captcha:SiteKey"] = "site-key",
                ["Captcha:SecretKey"] = "secret-key",
            }));
            builder.ConfigureTestServices(services => services.AddSingleton<ICaptchaVerifier>(_verifier));
        });

    [Theory]
    [InlineData("/Account/Login", "login")]
    [InlineData("/Account/Password", "password")]
    [InlineData("/Account/ForgotPassword", "reset")]
    public async Task Protected_forms_load_the_check_and_allow_only_googles_hosts(string page, string action)
    {
        using var factory = WithCaptcha();

        var response = await NewClient(factory).GetAsync(page);
        var html = await response.Content.ReadAsStringAsync();
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Contains($"data-captcha-action=\"{action}\"", html);
        Assert.Contains("https://www.google.com/recaptcha/api.js?render=site-key", html);
        Assert.Contains("Protected by reCAPTCHA", html);
        Assert.Contains("https://www.gstatic.com/recaptcha/", csp);
        Assert.Contains("frame-src https://www.google.com/recaptcha/", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
    }

    [Fact]
    public async Task Other_pages_keep_the_strict_policy()
    {
        using var factory = WithCaptcha();
        var client = NewClient(factory);
        await client.SignInAsDemoAsync("Customer");

        var csp = (await client.GetAsync("/Tickets")).Headers.GetValues("Content-Security-Policy").Single();

        Assert.DoesNotContain("google", csp);
    }

    [Fact]
    public async Task Without_keys_the_check_is_off_and_nothing_is_loaded()
    {
        var response = await NewClient().GetAsync("/Account/Login");

        Assert.DoesNotContain("recaptcha", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("google", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task A_form_without_a_passing_token_sends_no_code()
    {
        using var factory = WithCaptcha();

        var missing = await NewClient(factory).RequestCodeAsync("alice@example.test");
        var failing = await NewClient(factory).PostFormAsync("/Account/Login", "/Account/Login", new Dictionary<string, string>
        {
            ["Input.Email"] = "alice@example.test",
            [CaptchaGuard.FieldName] = "robot",
        });

        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        Assert.Contains("sent by a person", await failing.Content.ReadAsStringAsync());
        Assert.Empty(Factory.Mail.Messages);
    }

    [Fact]
    public async Task A_passing_token_lets_the_form_through()
    {
        using var factory = WithCaptcha();

        var response = await NewClient(factory).PostFormAsync("/Account/Login", "/Account/Login", new Dictionary<string, string>
        {
            ["Input.Email"] = "alice@example.test",
            [CaptchaGuard.FieldName] = "person",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Single(Factory.Mail.To("alice@example.test"));
        Assert.Equal(["login"], _verifier.Actions);
    }

    [Fact]
    public async Task Password_sign_in_and_reset_are_checked_with_their_own_action()
    {
        using var factory = WithCaptcha();
        await CreateUserAsync("Customer", "alice@example.test");

        var refused = await NewClient(factory).SignInAsync("alice@example.test", TestPassword);
        var signedIn = await NewClient(factory).PostFormAsync("/Account/Password", "/Account/Password", new Dictionary<string, string>
        {
            ["Input.Email"] = "alice@example.test",
            ["Input.Password"] = TestPassword,
            [CaptchaGuard.FieldName] = "person",
        });
        await NewClient(factory).PostFormAsync("/Account/ForgotPassword", "/Account/ForgotPassword", new Dictionary<string, string>
        {
            ["Input.Email"] = "alice@example.test",
            [CaptchaGuard.FieldName] = "person",
        });

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Contains("password", _verifier.Actions);
        Assert.Contains("reset", _verifier.Actions);
    }

    [Fact]
    public async Task The_demo_buttons_are_not_slowed_down_by_the_check()
    {
        using var factory = WithCaptcha();

        await NewClient(factory).SignInAsDemoAsync("Customer");

        Assert.Empty(_verifier.Actions);
    }
}

/// <summary>The part that talks to Google, with Google's answers played back.</summary>
public class RecaptchaVerifierTests
{
    private sealed class CannedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? SentForm { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SentForm = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static async Task<(CaptchaVerdict Verdict, CannedHandler Handler)> VerifyAsync(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new CannedHandler(status, body);
        using var http = new HttpClient(handler);
        var verifier = new RecaptchaVerifier(http, Options.Create(new CaptchaOptions { SiteKey = "site", SecretKey = "secret", MinimumScore = 0.5 }));
        return (await verifier.VerifyAsync("token-1", "login", "203.0.113.5"), handler);
    }

    [Fact]
    public async Task A_good_score_for_the_right_form_passes_and_the_secret_is_sent()
    {
        var (verdict, handler) = await VerifyAsync("""{"success":true,"score":0.9,"action":"login"}""");

        Assert.True(verdict.Passed);
        Assert.Contains("secret=secret", handler.SentForm);
        Assert.Contains("response=token-1", handler.SentForm);
        Assert.Contains("remoteip=203.0.113.5", handler.SentForm);
    }

    [Theory]
    [InlineData("""{"success":true,"score":0.3,"action":"login"}""", "score too low")]
    [InlineData("""{"success":true,"score":0.9,"action":"reset"}""", "another form")]
    [InlineData("""{"success":false,"error-codes":["timeout-or-duplicate"]}""", "timeout-or-duplicate")]
    [InlineData("not json", "unavailable")]
    public async Task Bad_answers_are_refused(string body, string reason)
    {
        var (verdict, _) = await VerifyAsync(body);

        Assert.False(verdict.Passed);
        Assert.Contains(reason, verdict.Reason);
    }

    [Fact]
    public async Task An_unreachable_service_refuses_the_form()
    {
        var (verdict, _) = await VerifyAsync(string.Empty, HttpStatusCode.ServiceUnavailable);

        Assert.False(verdict.Passed);
    }
}

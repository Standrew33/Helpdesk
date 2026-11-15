using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Security;

/// <summary>Google reCAPTCHA v3: an invisible check that scores how likely a form was sent by a person.</summary>
public class CaptchaOptions
{
    public const string SectionName = "Captcha";

    /// <summary>Public key used in the page.</summary>
    public string? SiteKey { get; set; }

    /// <summary>Secret key used by the server to ask Google about a token. Without both keys the check is off.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Scores run from 0.0 (a bot) to 1.0 (a person); Google suggests 0.5 to start with.</summary>
    public double MinimumScore { get; set; } = 0.5;

    public bool Enabled => !string.IsNullOrWhiteSpace(SiteKey) && !string.IsNullOrWhiteSpace(SecretKey);
}

public sealed record CaptchaVerdict(bool Passed, double Score, string Reason);

public interface ICaptchaVerifier
{
    Task<CaptchaVerdict> VerifyAsync(string token, string expectedAction, string? remoteIp, CancellationToken ct = default);
}

/// <summary>Asks Google's siteverify endpoint about a token from the page.</summary>
public sealed class RecaptchaVerifier(HttpClient http, IOptions<CaptchaOptions> options) : ICaptchaVerifier
{
    public static readonly Uri VerifyUrl = new("https://www.google.com/recaptcha/api/siteverify");

    public async Task<CaptchaVerdict> VerifyAsync(string token, string expectedAction, string? remoteIp, CancellationToken ct = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["secret"] = options.Value.SecretKey ?? string.Empty,
            ["response"] = token,
        };

        if (!string.IsNullOrEmpty(remoteIp))
        {
            fields["remoteip"] = remoteIp;
        }

        Answer? answer;

        try
        {
            using var response = await http.PostAsync(VerifyUrl, new FormUrlEncodedContent(fields), ct);
            response.EnsureSuccessStatusCode();
            answer = await response.Content.ReadFromJsonAsync<Answer>(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Without an answer nobody can tell a person from a program; the form is refused rather than let through.
            return new CaptchaVerdict(false, 0, "verification unavailable");
        }

        if (answer is not { Success: true })
        {
            return new CaptchaVerdict(false, 0, "token not accepted: " + string.Join(", ", answer?.ErrorCodes ?? []));
        }

        if (!string.Equals(answer.Action, expectedAction, StringComparison.Ordinal))
        {
            return new CaptchaVerdict(false, answer.Score, "token made for another form");
        }

        return answer.Score >= options.Value.MinimumScore
            ? new CaptchaVerdict(true, answer.Score, "ok")
            : new CaptchaVerdict(false, answer.Score, "score too low");
    }

    private sealed class Answer
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("score")]
        public double Score { get; set; }

        [JsonPropertyName("action")]
        public string? Action { get; set; }

        [JsonPropertyName("error-codes")]
        public string[]? ErrorCodes { get; set; }
    }
}

/// <summary>What a page calls before it acts on a protected form.</summary>
public partial class CaptchaGuard(ICaptchaVerifier verifier, IOptions<CaptchaOptions> options, ILogger<CaptchaGuard> logger)
{
    public const string FieldName = "captchaToken";

    public const string FailureMessage = "Verification failed. Please try again.";

    public async Task<bool> PassesAsync(HttpRequest request, string action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!options.Value.Enabled)
        {
            return true;
        }

        var token = request.HasFormContentType ? request.Form[FieldName].ToString() : string.Empty;

        var verdict = string.IsNullOrEmpty(token)
            ? new CaptchaVerdict(false, 0, "no token")
            : await verifier.VerifyAsync(token, action, request.HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        if (!verdict.Passed)
        {
            Refused(logger, action, verdict.Score.ToString("0.0", CultureInfo.InvariantCulture), verdict.Reason);
        }

        return verdict.Passed;
    }

    [LoggerMessage(EventId = 1015, Level = LogLevel.Warning, Message = "CAPTCHA refused the {Action} form (score {Score}): {Reason}")]
    private static partial void Refused(ILogger logger, string action, string score, string reason);
}

/// <summary>
/// Pages with a protected form may load Google's script and frame. Only these pages switch to the nonce-based
/// policy, and only when the check is switched on; every script tag of the page then carries the nonce.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CaptchaPageAttribute : Attribute, IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.HttpContext.RequestServices.GetRequiredService<IOptions<CaptchaOptions>>().Value.Enabled)
        {
            context.HttpContext.Response.Headers.ContentSecurityPolicy =
                SecurityHeaders.CaptchaContentSecurityPolicy(ScriptNonce.Issue(context.HttpContext));
        }

        await next();
    }
}

public static class CaptchaRegistration
{
    public static IServiceCollection AddCaptcha(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CaptchaOptions>(configuration.GetSection(CaptchaOptions.SectionName));
        services.AddHttpClient<ICaptchaVerifier, RecaptchaVerifier>(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<CaptchaGuard>();
        return services;
    }
}

using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Email;
using Helpdesk.Web.Security;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Features.SignIn;

public enum SendOutcome
{
    Sent,

    /// <summary>A code went out less than a minute ago; <see cref="SendResult.ChallengeId"/> is that one, when it still works.</summary>
    AlreadySent,
    TooMany,
    Failed,
}

public sealed record SendResult(SendOutcome Outcome, Guid? ChallengeId, TimeSpan RetryAfter);

/// <summary>Creates a code and link for an address and e-mails them.</summary>
public partial class CodeMailer(
    OneTimeCodeService codes,
    IEmailSender email,
    IOptions<SiteOptions> site,
    IOptions<OneTimeCodeOptions> options,
    ILogger<CodeMailer> logger)
{
    public async Task<SendResult> SendAsync(string address, ChallengePurpose purpose, HttpRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var issued = await codes.IssueAsync(address, purpose, ct);

        switch (issued.Outcome)
        {
            case IssueOutcome.TooSoon:
                return new SendResult(SendOutcome.AlreadySent, await codes.LatestOpenAsync(address, purpose, ct), issued.RetryAfter);
            case IssueOutcome.TooMany:
                return new SendResult(SendOutcome.TooMany, null, issued.RetryAfter);
        }

        var challenge = issued.Challenge!;
        var link = $"{BaseUrl(request)}/Account/Link?token={Uri.EscapeDataString(challenge.Token)}";

        try
        {
            await email.SendAsync(SignInEmails.For(address.Trim(), purpose, challenge, link, options.Value.Lifetime), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The address itself is not logged; the provider's own log has it if it is needed.
            SendFailed(logger, ex, purpose);
            return new SendResult(SendOutcome.Failed, null, TimeSpan.Zero);
        }

        return new SendResult(SendOutcome.Sent, challenge.Id, TimeSpan.Zero);
    }

    /// <summary>The configured address of the site; the request's own host only when none is configured (development, tests).</summary>
    private string BaseUrl(HttpRequest request)
        => !string.IsNullOrWhiteSpace(site.Value.PublicUrl)
            ? site.Value.PublicUrl.TrimEnd('/')
            : $"{request.Scheme}://{request.Host}{request.PathBase}";

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not send the {Purpose} e-mail")]
    private static partial void SendFailed(ILogger logger, Exception exception, ChallengePurpose purpose);
}

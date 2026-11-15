using Helpdesk.Web.Data;
using Helpdesk.Web.Features.SignIn;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Account;

/// <summary>Second step: the six-digit code from the e-mail.</summary>
[AllowAnonymous]
public class CodeModel(OneTimeCodeService codes, CodeMailer mailer, EmailSignIn emailSignIn) : PageModel
{
    /// <summary>One digit per box; a code pasted or typed into a single field arrives whole, which works as well.</summary>
    [BindProperty]
    public string[] Code { get; set; } = [];

    [TempData]
    public string? Notice { get; set; }

    public Guid ChallengeId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public ChallengePurpose Purpose { get; private set; }

    public int LifetimeMinutes => (int)emailSignIn.CodeLifetime.TotalMinutes;

    /// <summary>The code was used, expired, replaced or guessed wrong too often; only a new one helps.</summary>
    public bool NoLongerValid { get; private set; }

    public string? ReturnUrl { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid c, string? returnUrl = null, CancellationToken ct = default)
    {
        ReturnUrl = returnUrl;
        await LoadAsync(c, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid c, string? returnUrl = null, CancellationToken ct = default)
    {
        ReturnUrl = returnUrl;

        if (!await LoadAsync(c, ct))
        {
            return Page();
        }

        var result = await codes.VerifyCodeAsync(c, string.Concat(Code), ct);

        switch (result.Outcome)
        {
            case VerifyOutcome.WrongCode:
                ModelState.AddModelError(nameof(Code), "The code is not correct.");
                return Page();

            case VerifyOutcome.NoLongerValid:
                NoLongerValid = true;
                return Page();
        }

        var outcome = result.Purpose == ChallengePurpose.PasswordReset
            ? await emailSignIn.BeginPasswordResetAsync(HttpContext, result.Email!)
            : await emailSignIn.CompleteAsync(HttpContext, result.Email!, returnUrl);

        if (outcome == EmailSignInResult.Deactivated)
        {
            ModelState.AddModelError(string.Empty, "This account has been deactivated. Contact the support team if you need it again.");
            NoLongerValid = true;
            return Page();
        }

        return outcome switch
        {
            EmailSignInResult.NeedsProfile => RedirectToPage("Welcome"),
            EmailSignInResult.PasswordResetReady => RedirectToPage("NewPassword"),
            _ when !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) => LocalRedirect(returnUrl),
            _ => RedirectToPage("/Tickets/Index"),
        };
    }

    public async Task<IActionResult> OnPostResendAsync(Guid c, string? returnUrl = null, CancellationToken ct = default)
    {
        var challenge = await codes.FindAsync(c, ct);

        if (challenge is null)
        {
            return RedirectToPage("Login", new { returnUrl });
        }

        var sent = await mailer.SendAsync(challenge.Email.ToLowerInvariant(), challenge.Purpose, Request, ct);

        switch (sent.Outcome)
        {
            case SendOutcome.Sent:
                Notice = "New code sent.";
                return RedirectToPage(new { c = sent.ChallengeId, returnUrl });

            case SendOutcome.AlreadySent:
                Notice = CodeMailer.AlreadySentMessage(sent.RetryAfter);
                return RedirectToPage(new { c = sent.ChallengeId ?? c, returnUrl });

            case SendOutcome.TooMany:
                Notice = CodeMailer.TooManyMessage(sent.RetryAfter);
                return RedirectToPage(new { c, returnUrl });

            default:
                Notice = CodeMailer.FailedMessage;
                return RedirectToPage(new { c, returnUrl });
        }
    }

    private async Task<bool> LoadAsync(Guid id, CancellationToken ct)
    {
        ChallengeId = id;
        var challenge = await codes.FindOpenAsync(id, ct);

        if (challenge is null)
        {
            NoLongerValid = true;
            return false;
        }

        Purpose = challenge.Purpose;
        Email = challenge.Email.ToLowerInvariant();
        return true;
    }

}

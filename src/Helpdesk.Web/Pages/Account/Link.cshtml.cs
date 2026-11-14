using Helpdesk.Web.Data;
using Helpdesk.Web.Features.SignIn;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Account;

/// <summary>
/// The link from the e-mail. Opening it only shows a button: mail scanners and link previews open links on their own,
/// and must not use up the link or sign anyone in. The click does.
/// </summary>
[AllowAnonymous]
public class LinkModel(OneTimeCodeService codes, EmailSignIn emailSignIn) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    public string MaskedEmail { get; private set; } = string.Empty;

    public ChallengePurpose Purpose { get; private set; }

    public bool NoLongerValid { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var challenge = await codes.FindOpenByTokenAsync(Token ?? string.Empty, ct);

        if (challenge is null)
        {
            NoLongerValid = true;
            return Page();
        }

        MaskedEmail = SignInEmails.Mask(challenge.Email.ToLowerInvariant());
        Purpose = challenge.Purpose;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var result = await codes.VerifyTokenAsync(Token ?? string.Empty, ct);

        if (result.Outcome != VerifyOutcome.Success)
        {
            NoLongerValid = true;
            return Page();
        }

        var outcome = result.Purpose == ChallengePurpose.PasswordReset
            ? await emailSignIn.BeginPasswordResetAsync(HttpContext, result.Email!)
            : await emailSignIn.CompleteAsync(HttpContext, result.Email!, returnUrl: null);

        switch (outcome)
        {
            case EmailSignInResult.NeedsProfile:
                return RedirectToPage("Welcome");

            case EmailSignInResult.PasswordResetReady:
                return RedirectToPage("NewPassword");

            case EmailSignInResult.Deactivated:
                ModelState.AddModelError(string.Empty, "This account has been deactivated. Contact the support team if you need it again.");
                NoLongerValid = true;
                return Page();

            default:
                return RedirectToPage("/Tickets/Index");
        }
    }
}

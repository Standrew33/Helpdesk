using System.ComponentModel.DataAnnotations;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.SignIn;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Account;

/// <summary>
/// Starts a password reset: a code and link go to the address. As with sign-in, the answer is the same whether
/// or not the address has an account.
/// </summary>
[AllowAnonymous]
[CaptchaPage]
public class ForgotPasswordModel(CodeMailer mailer, CaptchaGuard captcha, ILogger<ForgotPasswordModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(Input.Website))
        {
            SecurityLog.RegistrationTrapped(logger);
            return RedirectToPage();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!await captcha.PassesAsync(Request, "reset", ct))
        {
            ModelState.AddModelError(string.Empty, CaptchaGuard.FailureMessage);
            return Page();
        }

        var email = Input.Email.Trim();

        var sent = await mailer.SendAsync(email, ChallengePurpose.PasswordReset, Request, ct);

        switch (sent.Outcome)
        {
            case SendOutcome.Sent:
            case SendOutcome.AlreadySent when sent.ChallengeId is not null:
                return RedirectToPage("Code", new { c = sent.ChallengeId });

            case SendOutcome.AlreadySent:
                ModelState.AddModelError(string.Empty, CodeMailer.AlreadySentMessage(sent.RetryAfter));
                return Page();

            case SendOutcome.TooMany:
                ModelState.AddModelError(string.Empty, CodeMailer.TooManyMessage(sent.RetryAfter));
                return Page();

            default:
                ModelState.AddModelError(string.Empty, CodeMailer.FailedMessage);
                return Page();
        }
    }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        [StringLength(256)]
        [Display(Name = "E-mail")]
        public string Email { get; set; } = string.Empty;

        /// <summary>Hidden from people by the style sheet. A filled-in value means a program submitted the form.</summary>
        [StringLength(200)]
        public string? Website { get; set; }
    }
}

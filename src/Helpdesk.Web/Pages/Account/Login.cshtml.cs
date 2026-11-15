using System.ComponentModel.DataAnnotations;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.SignIn;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Pages.Account;

/// <summary>
/// First step of signing in or signing up: the e-mail address. A code and a link go to that address whether or
/// not it has an account, so the answer never tells which addresses are registered.
/// </summary>
[AllowAnonymous]
[CaptchaPage]
public class LoginModel(
    CodeMailer mailer,
    CaptchaGuard captcha,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IOptions<DemoOptions> demoOptions,
    ILogger<LoginModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? Notice { get; set; }

    public bool DemoEnabled => demoOptions.Value.Enabled;

    public string? ReturnUrl { get; private set; }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null, CancellationToken ct = default)
    {
        ReturnUrl = returnUrl;

        // A person never sees the hidden "Website" field; only a program that fills in every field does.
        if (!string.IsNullOrEmpty(Input.Website))
        {
            SecurityLog.RegistrationTrapped(logger);
            return RedirectToPage();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!await captcha.PassesAsync(Request, "login", ct))
        {
            ModelState.AddModelError(string.Empty, CaptchaGuard.FailureMessage);
            return Page();
        }

        var email = Input.Email.Trim();

        if (DatabaseSeeder.IsDemoAddress(email))
        {
            ModelState.AddModelError("Input.Email", "This address belongs to the demo. Use your own e-mail address.");
            return Page();
        }

        var sent = await mailer.SendAsync(email, ChallengePurpose.SignIn, Request, ct);

        switch (sent.Outcome)
        {
            case SendOutcome.Sent:
                return RedirectToPage("Code", new { c = sent.ChallengeId, returnUrl });

            case SendOutcome.AlreadySent when sent.ChallengeId is { } open:
                Notice = "Code already sent. Check your e-mail.";
                return RedirectToPage("Code", new { c = open, returnUrl });

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

    public async Task<IActionResult> OnPostDemoAsync(string role, string? returnUrl = null)
    {
        if (!DemoEnabled || !RoleNames.All.Contains(role))
        {
            return NotFound();
        }

        var user = await userManager.FindByEmailAsync(DatabaseSeeder.DemoEmail(role));

        if (user is not { IsActive: true })
        {
            return NotFound();
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        SecurityLog.SignedIn(logger, user.Id);
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToPage("/Tickets/Index");
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

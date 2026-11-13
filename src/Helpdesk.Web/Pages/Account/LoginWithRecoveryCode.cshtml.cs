using System.ComponentModel.DataAnnotations;
using Helpdesk.Web.Data;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Account;

/// <summary>Signs in with one of the single-use recovery codes when the authenticator app is not at hand.</summary>
[AllowAnonymous]
public class LoginWithRecoveryCodeModel(SignInManager<ApplicationUser> signInManager, ILogger<LoginWithRecoveryCodeModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        return await signInManager.GetTwoFactorAuthenticationUserAsync() is null ? RedirectToPage("Login") : Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;

        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();

        if (user is null)
        {
            return RedirectToPage("Login");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var code = Input.RecoveryCode.Replace(" ", string.Empty, StringComparison.Ordinal);
        var result = await signInManager.TwoFactorRecoveryCodeSignInAsync(code);

        if (result.Succeeded)
        {
            SecurityLog.RecoveryCodeUsed(logger, user.Id);
            return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToPage("/Tickets/Index");
        }

        if (result.IsLockedOut)
        {
            SecurityLog.LockedOut(logger, user.Id);
            ModelState.AddModelError(string.Empty, "The account is temporarily locked. Try again later.");
        }
        else
        {
            SecurityLog.SignInFailed(logger, user.Id);
            ModelState.AddModelError("Input.RecoveryCode", "The recovery code is not correct or was already used.");
        }

        return Page();
    }

    public class InputModel
    {
        [Required]
        [StringLength(30, MinimumLength = 8)]
        [Display(Name = "Recovery code")]
        public string RecoveryCode { get; set; } = string.Empty;
    }
}

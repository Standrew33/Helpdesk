using System.ComponentModel.DataAnnotations;
using Helpdesk.Web.Data;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Account;

/// <summary>The second sign-in step: the code from the authenticator app. Reached only after a correct password.</summary>
[AllowAnonymous]
public class LoginWith2faModel(SignInManager<ApplicationUser> signInManager, ILogger<LoginWith2faModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool RememberMe { get; private set; }

    public string? ReturnUrl { get; private set; }

    public async Task<IActionResult> OnGetAsync(bool rememberMe, string? returnUrl = null)
    {
        RememberMe = rememberMe;
        ReturnUrl = returnUrl;
        return await signInManager.GetTwoFactorAuthenticationUserAsync() is null ? RedirectToPage("Login") : Page();
    }

    public async Task<IActionResult> OnPostAsync(bool rememberMe, string? returnUrl = null)
    {
        RememberMe = rememberMe;
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

        var code = Input.Code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(code, rememberMe, rememberClient: false);

        if (result.Succeeded)
        {
            SecurityLog.SignedIn(logger, user.Id);
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
            ModelState.AddModelError("Input.Code", "The code is not correct.");
        }

        return Page();
    }

    public class InputModel
    {
        [Required]
        [StringLength(10, MinimumLength = 6)]
        [Display(Name = "Code from the app")]
        public string Code { get; set; } = string.Empty;
    }
}

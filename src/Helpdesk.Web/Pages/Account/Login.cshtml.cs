using System.ComponentModel.DataAnnotations;
using Helpdesk.Web.Data;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Pages.Account;

[AllowAnonymous]
public class LoginModel(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    SignInTimingGuard timing,
    IOptions<DemoOptions> demoOptions,
    ILogger<LoginModel> logger) : PageModel
{
    /// <summary>One message for every failure, so it does not reveal whether the address has an account or is locked.</summary>
    public const string FailureMessage = "Invalid e-mail or password, or the account is temporarily locked. Try again later.";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool DemoEnabled => demoOptions.Value.Enabled;

    public string? ReturnUrl { get; private set; }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Input.Email.Trim());

        if (user is { IsActive: true })
        {
            var result = await signInManager.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                SecurityLog.SignedIn(logger, user.Id);
                return RedirectAfterLogin(returnUrl);
            }

            if (result.RequiresTwoFactor)
            {
                return RedirectToPage("LoginWith2fa", new { returnUrl, rememberMe = Input.RememberMe });
            }

            if (result.IsLockedOut)
            {
                // A locked account is refused before its password is checked; do the work anyway so the answer is not quicker.
                timing.SpendTheSameTime(Input.Password);
                SecurityLog.LockedOut(logger, user.Id);
            }
            else
            {
                SecurityLog.SignInFailed(logger, user.Id);
            }
        }
        else
        {
            timing.SpendTheSameTime(Input.Password);
            SecurityLog.SignInFailedUnknown(logger);
        }

        ModelState.AddModelError(string.Empty, FailureMessage);
        return Page();
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
        return RedirectAfterLogin(returnUrl);
    }

    private IActionResult RedirectAfterLogin(string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToPage("/Tickets/Index");

    public class InputModel
    {
        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [StringLength(256)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.SignIn;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Account;

/// <summary>Last step of a password reset, reached only with an address proven by a reset code or link.</summary>
[AllowAnonymous]
public class NewPasswordModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    EmailSignIn emailSignIn,
    AccountAccess access,
    ILogger<NewPasswordModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string Email { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
        => await LoadAsync() is null ? RedirectToPage("ForgotPassword") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await LoadAsync();
        var pending = emailSignIn.ReadPendingReset(HttpContext);

        if (user is null || pending is null)
        {
            return RedirectToPage("ForgotPassword");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Also renews the security stamp, which signs the account out everywhere else.
        var result = await userManager.ResetPasswordAsync(user, pending.Token, Input.Password);

        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "InvalidToken"))
            {
                emailSignIn.ForgetPendingReset(HttpContext);
                return RedirectToPage("ForgotPassword");
            }

            ModelState.AddModelError("Input.Password", string.Join(" ", result.Errors.Select(e => e.Description).Distinct()));
            return Page();
        }

        // The owner proved the address; failed guesses by someone else no longer keep them out.
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);

        emailSignIn.ForgetPendingReset(HttpContext);
        SecurityLog.PasswordChanged(logger, user.Id);
        await signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToPage("/Tickets/Index");
    }

    private async Task<ApplicationUser?> LoadAsync()
    {
        var pending = emailSignIn.ReadPendingReset(HttpContext);
        var user = pending is null ? null : await userManager.FindByIdAsync(pending.UserId);

        if (user is null || !await access.AllowsAsync(user))
        {
            return null;
        }

        Email = user.Email ?? string.Empty;
        DisplayName = user.DisplayName;
        return user;
    }

    public class InputModel
    {
        [Required]
        [DataType(DataType.Password)]
        [StringLength(256)]
        [Display(Name = "New password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Repeat new password")]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}

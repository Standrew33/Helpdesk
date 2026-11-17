using System.ComponentModel.DataAnnotations;
using Helpdesk.Web.Data;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Pages.Account;

public class SettingsModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILogger<SettingsModel> logger) : PageModel
{
    // The two forms are bound per handler, so one form never fails validation because of the other.
    public ProfileInput Profile { get; private set; } = new();

    public PasswordInput Password { get; private set; } = new();

    public NewPasswordInput NewPassword { get; private set; } = new();

    /// <summary>False for accounts created with an e-mail code that never set a password.</summary>
    public bool HasPassword { get; private set; }

    [TempData]
    public string? Notice { get; set; }

    public string Email { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await LoadAsync();

        if (user is null)
        {
            return Challenge();
        }

        Profile.DisplayName = user.DisplayName;
        return Page();
    }

    public async Task<IActionResult> OnPostProfileAsync([Bind(Prefix = nameof(Profile))] ProfileInput input)
    {
        var user = await LoadAsync();

        if (user is null)
        {
            return Forbid();
        }

        Profile = input;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        user.DisplayName = input.DisplayName.Trim();
        await userManager.UpdateAsync(user);

        // The name is part of the sign-in cookie, so sign in again to show the new one right away.
        await signInManager.RefreshSignInAsync(user);
        Notice = "Name updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPasswordAsync([Bind(Prefix = nameof(Password))] PasswordInput input)
    {
        var user = await LoadAsync();

        if (user is null)
        {
            return Forbid();
        }

        Profile.DisplayName = user.DisplayName;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await userManager.ChangePasswordAsync(user, input.CurrentPassword, input.NewPassword);

        if (!result.Succeeded)
        {
            // Guessing the current password here must not be easier than guessing it at the sign-in page.
            if (result.Errors.Any(e => e.Code == "PasswordMismatch"))
            {
                await userManager.AccessFailedAsync(user);
            }

            // A field shows one message, so the problems of one field are joined into a single sentence.
            foreach (var field in result.Errors.GroupBy(e => e.Code == "PasswordMismatch" ? "Password.CurrentPassword" : "Password.NewPassword"))
            {
                ModelState.AddModelError(field.Key, string.Join(" ", field.Select(e => e.Description)));
            }

            return Page();
        }

        SecurityLog.PasswordChanged(logger, user.Id);
        await signInManager.RefreshSignInAsync(user);
        Notice = "Password changed.";
        return RedirectToPage();
    }

    /// <summary>Accounts created with an e-mail code have no password; their owner can add one as a second way in.</summary>
    public async Task<IActionResult> OnPostSetPasswordAsync([Bind(Prefix = nameof(NewPassword))] NewPasswordInput input)
    {
        var user = await LoadAsync();

        if (user is null || HasPassword)
        {
            return Forbid();
        }

        Profile.DisplayName = user.DisplayName;
        NewPassword = input;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await userManager.AddPasswordAsync(user, input.Password);

        if (!result.Succeeded)
        {
            ModelState.AddModelError("NewPassword.Password", string.Join(" ", result.Errors.Select(e => e.Description).Distinct()));
            return Page();
        }

        SecurityLog.PasswordChanged(logger, user.Id);
        await signInManager.RefreshSignInAsync(user);
        Notice = "Password set.";
        return RedirectToPage();
    }

    private async Task<ApplicationUser?> LoadAsync()
    {
        var user = await userManager.GetUserAsync(User);

        if (user is not null)
        {
            Email = user.Email ?? string.Empty;
            HasPassword = await userManager.HasPasswordAsync(user);
        }

        return user;
    }

    public class ProfileInput
    {
        [Required]
        [StringLength(100)]
        [SafeText]
        [Display(Name = "Your name")]
        public string DisplayName { get; set; } = string.Empty;
    }

    public class PasswordInput
    {
        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Repeat new password")]
        [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class NewPasswordInput
    {
        [Required]
        [DataType(DataType.Password)]
        [StringLength(256)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Repeat password")]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}

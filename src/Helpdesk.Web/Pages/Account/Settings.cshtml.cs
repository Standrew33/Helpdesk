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
    IOptions<DemoOptions> demo,
    ILogger<SettingsModel> logger) : PageModel
{
    // The two forms are bound per handler, so one form never fails validation because of the other.
    public ProfileInput Profile { get; private set; } = new();

    public PasswordInput Password { get; private set; } = new();

    [TempData]
    public string? Notice { get; set; }

    public string Email { get; private set; } = string.Empty;

    public bool TwoFactorEnabled { get; private set; }

    /// <summary>The public demo accounts are shared by every visitor, so they cannot be edited.</summary>
    public bool IsReadOnly { get; private set; }

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

        if (user is null || IsReadOnly)
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
        Notice = "Your name was updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPasswordAsync([Bind(Prefix = nameof(Password))] PasswordInput input)
    {
        var user = await LoadAsync();

        if (user is null || IsReadOnly)
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
        Notice = "Your password was changed.";
        return RedirectToPage();
    }

    private async Task<ApplicationUser?> LoadAsync()
    {
        var user = await userManager.GetUserAsync(User);

        if (user is not null)
        {
            Email = user.Email ?? string.Empty;
            TwoFactorEnabled = await userManager.GetTwoFactorEnabledAsync(user);
            IsReadOnly = demo.Value.Enabled && DatabaseSeeder.IsDemoAddress(user.Email);
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
}

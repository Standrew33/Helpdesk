using System.ComponentModel.DataAnnotations;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.SignIn;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Account;

/// <summary>
/// Last step for a new address: the account is created only now, after the address was proven with a code or link,
/// so nobody can register an address that is not theirs.
/// </summary>
[AllowAnonymous]
public class WelcomeModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    EmailSignIn emailSignIn,
    ILogger<WelcomeModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string Email { get; private set; } = string.Empty;

    public IActionResult OnGet()
    {
        var pending = emailSignIn.ReadPendingSignUp(HttpContext);

        if (pending is null)
        {
            return RedirectToPage("Login");
        }

        Email = pending.Email;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var pending = emailSignIn.ReadPendingSignUp(HttpContext);

        if (pending is null)
        {
            return RedirectToPage("Login");
        }

        Email = pending.Email;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Email);

        if (user is null)
        {
            user = new ApplicationUser { UserName = Email, Email = Email, EmailConfirmed = true, DisplayName = Input.DisplayName.Trim() };

            var created = string.IsNullOrEmpty(Input.Password)
                ? await userManager.CreateAsync(user)
                : await userManager.CreateAsync(user, Input.Password);

            if (!created.Succeeded)
            {
                // A field shows one message, so the problems of one field are joined into a single sentence.
                foreach (var field in created.Errors.GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? "Input.Password" : string.Empty))
                {
                    ModelState.AddModelError(field.Key, string.Join(" ", field.Select(e => e.Description).Distinct()));
                }

                return Page();
            }

            // Everyone who signs up by themselves is a customer; staff roles are given by an administrator.
            var inRole = await userManager.AddToRoleAsync(user, RoleNames.Customer);

            if (!inRole.Succeeded)
            {
                await userManager.DeleteAsync(user);
                ModelState.AddModelError(string.Empty, "The account could not be created. Please try again.");
                return Page();
            }

            SecurityLog.Registered(logger, user.Id);
        }
        else if (!user.IsActive)
        {
            return RedirectToPage("Login");
        }

        emailSignIn.ForgetPendingSignUp(HttpContext);
        await signInManager.SignInAsync(user, isPersistent: false);
        SecurityLog.SignedIn(logger, user.Id);

        return !string.IsNullOrEmpty(pending.ReturnUrl) && Url.IsLocalUrl(pending.ReturnUrl)
            ? LocalRedirect(pending.ReturnUrl)
            : RedirectToPage("/Tickets/Index");
    }

    public class InputModel
    {
        [Required]
        [StringLength(100)]
        [SafeText]
        [Display(Name = "Your name")]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Optional: the e-mail code always works, a password is a second way in.</summary>
        [DataType(DataType.Password)]
        [StringLength(256)]
        [Display(Name = "Password (optional)")]
        public string? Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Repeat password")]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        public string? ConfirmPassword { get; set; }
    }
}

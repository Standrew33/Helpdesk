using System.ComponentModel.DataAnnotations;
using Helpdesk.Core;
using Helpdesk.Core.Workspaces;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.SignIn;
using Helpdesk.Web.Features.Workspaces;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Account;

/// <summary>
/// Last step for a new address: the account is created only now, after the address was proven with a code or link,
/// so nobody can register an address that is not theirs. It comes with a help desk of its own, run by the new user.
/// </summary>
[AllowAnonymous]
public class WelcomeModel(
    UserManager<ApplicationUser> userManager,
    WorkspaceSignUp workspaceSignUp,
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

    /// <param name="skip">Create the account with nothing but the address; whatever was typed is ignored.</param>
    public async Task<IActionResult> OnPostAsync(bool skip = false)
    {
        var pending = emailSignIn.ReadPendingSignUp(HttpContext);

        if (pending is null)
        {
            return RedirectToPage("Login");
        }

        Email = pending.Email;

        if (skip)
        {
            ModelState.Clear();
            Input = new InputModel();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.FindByEmailAsync(Email);

        if (user is null)
        {
            var displayName = NameFor(Input.DisplayName, Email);
            user = new ApplicationUser { UserName = Email, Email = Email, EmailConfirmed = true, DisplayName = displayName };
            var workspaceName = string.IsNullOrWhiteSpace(Input.WorkspaceName) ? WorkspaceSignUp.DefaultName(displayName) : Input.WorkspaceName;

            IdentityResult created;

            try
            {
                created = await workspaceSignUp.CreateAsync(user, Input.Password, workspaceName);
            }
            catch (DomainException ex)
            {
                ModelState.AddModelError("Input.WorkspaceName", ex.Message);
                return Page();
            }

            if (!created.Succeeded)
            {
                // A field shows one message, so the problems of one field are joined into a single sentence.
                foreach (var field in created.Errors.GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? "Input.Password" : string.Empty))
                {
                    ModelState.AddModelError(field.Key, string.Join(" ", field.Select(e => e.Description).Distinct()));
                }

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

    /// <summary>Without a name the part of the address before the @ stands in; it can be changed in the settings.</summary>
    private static string NameFor(string? name, string email)
        => string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name.Trim();

    public class InputModel
    {
        [StringLength(100)]
        [SafeText]
        [Display(Name = "Your name (optional)")]
        public string? DisplayName { get; set; }

        [StringLength(Workspace.MaxNameLength)]
        [SafeText]
        [Display(Name = "Name of your help desk (optional)")]
        public string? WorkspaceName { get; set; }

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

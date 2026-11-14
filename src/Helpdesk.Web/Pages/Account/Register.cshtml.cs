using System.ComponentModel.DataAnnotations;
using Helpdesk.Web.Data;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Account;

[AllowAnonymous]
public class RegisterModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILogger<RegisterModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IActionResult OnGet()
        => User.Identity?.IsAuthenticated == true ? RedirectToPage("/Tickets/Index") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        // A person never sees the hidden "Website" field; only a program that fills in every field does.
        if (!string.IsNullOrEmpty(Input.Website))
        {
            SecurityLog.RegistrationTrapped(logger);
            return RedirectToPage("Login");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = Input.Email.Trim();

        // The demo addresses belong to the built-in demo users.
        if (DatabaseSeeder.IsDemoAddress(email))
        {
            ModelState.AddModelError("Input.Email", "This address is reserved. Use your own e-mail address.");
            return Page();
        }

        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = Input.DisplayName.Trim() };

        var created = await userManager.CreateAsync(user, Input.Password);

        if (!created.Succeeded)
        {
            // A field shows one message, so the problems of one field are joined into a single sentence.
            foreach (var field in created.Errors.GroupBy(e => FieldFor(e.Code)))
            {
                ModelState.AddModelError(field.Key, string.Join(" ", field.Select(MessageFor).Distinct()));
            }

            return Page();
        }

        // Everyone who registers by themselves is a customer; staff roles are given by an administrator.
        var inRole = await userManager.AddToRoleAsync(user, RoleNames.Customer);

        if (!inRole.Succeeded)
        {
            await userManager.DeleteAsync(user);
            ModelState.AddModelError(string.Empty, "The account could not be created. Please try again.");
            return Page();
        }

        SecurityLog.Registered(logger, user.Id);
        await signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToPage("/Tickets/Index");
    }

    private static string FieldFor(string code) => code switch
    {
        "DuplicateUserName" or "DuplicateEmail" or "InvalidEmail" or "InvalidUserName" => "Input.Email",
        _ when code.StartsWith("Password", StringComparison.Ordinal) => "Input.Password",
        _ => string.Empty,
    };

    private static string MessageFor(IdentityError error)
        => error.Code is "DuplicateUserName" or "DuplicateEmail"
            ? "An account with this e-mail address already exists."
            : error.Description;

    public class InputModel
    {
        [Required]
        [StringLength(100)]
        [SafeText]
        [Display(Name = "Your name")]
        public string DisplayName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [StringLength(256)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Repeat password")]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        /// <summary>Hidden from people by the style sheet. A filled-in value means a program submitted the form.</summary>
        [StringLength(200)]
        public string? Website { get; set; }
    }
}

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
    IOptions<DemoOptions> demoOptions) : PageModel
{
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

        var user = await userManager.FindByEmailAsync(Input.Email);

        if (user is { IsActive: true })
        {
            var result = await signInManager.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                return RedirectAfterLogin(returnUrl);
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "The account is temporarily locked. Try again later.");
                return Page();
            }
        }

        ModelState.AddModelError(string.Empty, "Invalid e-mail or password.");
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
        return RedirectAfterLogin(returnUrl);
    }

    private IActionResult RedirectAfterLogin(string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToPage("/Tickets/Index");

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}


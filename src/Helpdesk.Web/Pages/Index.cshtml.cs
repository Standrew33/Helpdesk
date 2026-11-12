using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages;

/// <summary>The start address: signed-in users land on the ticket list, everyone else on the sign-in page.</summary>
[AllowAnonymous]
public class IndexModel : PageModel
{
    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true
        ? RedirectToPage("/Tickets/Index")
        : RedirectToPage("/Account/Login");
}

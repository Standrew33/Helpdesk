using Helpdesk.Web.Features.SignIn;
using Helpdesk.Web.Features.Workspaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages;

/// <summary>
/// The public link of a help desk. It only remembers which help desk the visitor came for and sends them to the
/// e-mail sign-in; the account made after the code joins that help desk as a customer.
/// </summary>
[AllowAnonymous]
public class JoinModel(JoinLinkService joinLinks, EmailSignIn emailSignIn) : PageModel
{
    [FromRoute]
    public string? Code { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        // A signed-in person already belongs to a help desk.
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Tickets/Index");
        }

        var join = await joinLinks.FindOpenAsync(Code, ct);

        if (join is null)
        {
            return Page();
        }

        emailSignIn.RememberJoin(HttpContext, join.Code);
        return RedirectToPage("/Account/Login");
    }
}

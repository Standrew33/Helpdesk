using Helpdesk.Web.Features.SignIn;
using Helpdesk.Web.Features.Workspaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Account;

/// <summary>
/// The link from an invitation e-mail. Opening it only shows what the invitation is and a button: mail scanners and
/// link previews open links on their own and must not accept anything. The click does.
/// </summary>
[AllowAnonymous]
public class InvitationModel(InvitationService invitations, EmailSignIn emailSignIn) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    public OpenInvitation? Invitation { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
        => Invitation = await invitations.FindOpenAsync(Token, ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        Invitation = await invitations.FindOpenAsync(Token, ct);

        // One address belongs to one help desk; the page explains it instead of going on.
        if (Invitation is null || Invitation.AddressHasAccount)
        {
            return Page();
        }

        emailSignIn.BeginInvitedSignUp(HttpContext, Invitation.Email, Invitation.Id);
        return RedirectToPage("Welcome");
    }
}

using System.ComponentModel.DataAnnotations;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Features.Workspaces;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Pages;

/// <summary>Staff invite people into their help desk and see who has not answered yet.</summary>
[Authorize(Policy = Policies.Staff)]
public class InvitationsModel(InvitationService invitations, IOptions<SiteOptions> site) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? Error { get; set; }

    [TempData]
    public string? Notice { get; set; }

    public IReadOnlyList<InvitationRow> Waiting { get; private set; } = [];

    public IReadOnlyList<string> Roles => InvitationService.RolesFor(User.ToActor().Role);

    public async Task OnGetAsync(CancellationToken ct)
        => Waiting = await invitations.ListWaitingAsync(User.ToActor(), ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await invitations.InviteAsync(User.ToActor(), Input.Email, Input.Role, site.Value.BaseUrl(Request), ct);

            if (result.Succeeded)
            {
                Notice = $"Invitation sent to {Input.Email.Trim().ToLowerInvariant()}.";
                return RedirectToPage();
            }

            ModelState.AddModelError(string.Empty, result.Message ?? "The invitation could not be sent.");
        }

        Waiting = await invitations.ListWaitingAsync(User.ToActor(), ct);
        return Page();
    }

    public async Task<IActionResult> OnPostWithdrawAsync(Guid id, CancellationToken ct)
    {
        var result = await invitations.WithdrawAsync(User.ToActor(), id, ct);

        if (result.Outcome == ServiceOutcome.NotFound)
        {
            return NotFound();
        }

        if (result.Succeeded)
        {
            Notice = "Invitation withdrawn.";
        }
        else
        {
            Error = result.Message;
        }

        return RedirectToPage();
    }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        [StringLength(256)]
        [Display(Name = "E-mail")]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = RoleNames.Customer;
    }
}

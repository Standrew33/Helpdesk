using System.ComponentModel.DataAnnotations;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Features.Toasts;
using Helpdesk.Web.Features.Workspaces;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Pages;

/// <summary>Staff invite people into their help desk and see who has not answered yet.</summary>
[Authorize(Policy = Policies.Staff)]
public class InvitationsModel(InvitationService invitations, JoinLinkService joinLinks, IOptions<SiteOptions> site) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<InvitationRow> Waiting { get; private set; } = [];

    public IReadOnlyList<string> Roles => InvitationService.RolesFor(User.ToActor().Role);

    /// <summary>The public link of the help desk, through which customers sign up by themselves.</summary>
    public JoinLink JoinLink { get; private set; } = null!;

    public bool CanChangeJoinLink => User.IsInRole(RoleNames.Admin);

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public Task<IActionResult> OnPostJoinLinkAsync(bool open, CancellationToken ct)
        => ChangeJoinLinkAsync(joinLinks.SetOpenAsync(User.ToActor(), open, ct), open ? "The link for customers is turned on." : "The link for customers is turned off.");

    public Task<IActionResult> OnPostNewJoinLinkAsync(CancellationToken ct)
        => ChangeJoinLinkAsync(joinLinks.RenewAsync(User.ToActor(), ct), "The help desk has a new link for customers; the old one no longer works.");

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await invitations.InviteAsync(User.ToActor(), Input.Email, Input.Role, site.Value.BaseUrl(Request), ct);

            if (result.Succeeded)
            {
                TempData.Success($"Invitation sent to {Input.Email.Trim().ToLowerInvariant()}.");
                return RedirectToPage();
            }

            ModelState.AddModelError(string.Empty, result.Message ?? "The invitation could not be sent.");
        }

        await LoadAsync(ct);
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
            TempData.Success("Invitation withdrawn.");
        }
        else
        {
            TempData.Failure(result.Message ?? "The change could not be saved.");
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Waiting = await invitations.ListWaitingAsync(User.ToActor(), ct);
        JoinLink = await joinLinks.GetAsync(User.ToActor(), site.Value.BaseUrl(Request), ct);
    }

    private async Task<IActionResult> ChangeJoinLinkAsync(Task<ServiceResult> change, string success)
    {
        var result = await change;

        if (result.Succeeded)
        {
            TempData.Success(success);
        }
        else
        {
            TempData.Failure(result.Message ?? "The change could not be saved.");
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

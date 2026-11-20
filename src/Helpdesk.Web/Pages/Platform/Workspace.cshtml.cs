using System.Security.Claims;
using Helpdesk.Web.Features.Platform;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Features.Toasts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Platform;

/// <summary>One help desk as its owner sees it: its people, who can be switched off, and its tickets.</summary>
public class WorkspaceModel(PlatformService platform) : PageModel
{
    [FromRoute]
    public int Id { get; set; }

    public WorkspaceOverview Overview { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var overview = await platform.GetWorkspaceAsync(Id, ct);

        if (overview is null)
        {
            return NotFound();
        }

        Overview = overview;
        return Page();
    }

    public async Task<IActionResult> OnPostBlockAsync(bool blocked, CancellationToken ct)
    {
        var result = await platform.SetWorkspaceBlockedAsync(OwnerId, Id, blocked, ct);

        if (result.Outcome == ServiceOutcome.NotFound)
        {
            return NotFound();
        }

        TempData.Success(blocked ? "The workspace is deactivated; its people are signed out." : "The workspace is active again.");
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostActiveAsync(string userId, bool active)
    {
        var result = await platform.SetUserActiveAsync(OwnerId, userId, active);

        if (result.Outcome == ServiceOutcome.NotFound)
        {
            return NotFound();
        }

        if (result.Succeeded)
        {
            TempData.Success(active ? "Account activated." : "Account deactivated; the person is signed out.");
        }
        else
        {
            TempData.Failure(result.Message ?? "The change could not be saved.");
        }

        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(string userId)
    {
        var result = await platform.DeleteUserAsync(OwnerId, userId);

        if (result.Outcome == ServiceOutcome.NotFound)
        {
            return NotFound();
        }

        if (result.Succeeded)
        {
            TempData.Success("Account deleted. What the person wrote stays, signed \"Deleted user\".");
        }
        else
        {
            TempData.Failure(result.Message ?? "The change could not be saved.");
        }

        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteWorkspaceAsync(string? confirmName, CancellationToken ct)
    {
        var result = await platform.DeleteWorkspaceAsync(OwnerId, Id, confirmName, ct);

        if (result.Outcome == ServiceOutcome.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            TempData.Failure(result.Message ?? "The change could not be saved.");
            return RedirectToPage(new { id = Id });
        }

        TempData.Success("The workspace is deleted with everything in it.");
        return RedirectToPage("Index");
    }

    private string OwnerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}

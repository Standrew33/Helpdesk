using System.Security.Claims;
using Helpdesk.Web.Features.Platform;
using Helpdesk.Web.Features.Tickets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Platform;

/// <summary>One help desk as its owner sees it: its people, who can be switched off, and its tickets.</summary>
public class WorkspaceModel(PlatformService platform) : PageModel
{
    [FromRoute]
    public int Id { get; set; }

    [TempData]
    public string? Error { get; set; }

    [TempData]
    public string? Notice { get; set; }

    public WorkspaceOverview Overview { get; private set; } = null!;

    public int MaxTicketsShown => PlatformService.MaxTicketsShown;

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

        Notice = blocked ? "The workspace is blocked; its people are signed out." : "The workspace works again.";
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
            Notice = active ? "Account activated." : "Account deactivated; the person is signed out.";
        }
        else
        {
            Error = result.Message;
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
            Notice = "Account deleted. What the person wrote stays, signed \"Deleted user\".";
        }
        else
        {
            Error = result.Message;
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
            Error = result.Message;
            return RedirectToPage(new { id = Id });
        }

        TempData["Notice"] = "The workspace is deleted with everything in it.";
        return RedirectToPage("Index");
    }

    private string OwnerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}

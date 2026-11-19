using System.Security.Claims;
using Helpdesk.Web.Features.Platform;
using Helpdesk.Web.Features.Tickets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Platform;

/// <summary>Every help desk on the platform, for its owner.</summary>
public class IndexModel(PlatformService platform) : PageModel
{
    [TempData]
    public string? Error { get; set; }

    [TempData]
    public string? Notice { get; set; }

    public IReadOnlyList<WorkspaceRow> Workspaces { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct) => Workspaces = await platform.ListWorkspacesAsync(ct);

    public async Task<IActionResult> OnPostBlockAsync(int id, bool blocked, CancellationToken ct)
    {
        var result = await platform.SetWorkspaceBlockedAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, id, blocked, ct);

        if (result.Outcome == ServiceOutcome.NotFound)
        {
            return NotFound();
        }

        Notice = blocked ? "The workspace is deactivated; its people are signed out." : "The workspace is active again.";
        return RedirectToPage();
    }
}

using Helpdesk.Web.Features.Platform;
using Helpdesk.Web.Features.Tickets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Platform;

/// <summary>A ticket of any help desk, read-only: the owner of the platform looks, the help desk itself works on it.</summary>
public class TicketModel(PlatformService platform) : PageModel
{
    [FromRoute]
    public int WorkspaceId { get; set; }

    [FromRoute]
    public int Number { get; set; }

    public string WorkspaceName { get; private set; } = string.Empty;

    public TicketDetails Details { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var found = await platform.GetTicketAsync(WorkspaceId, Number, ct);

        if (found is null)
        {
            return NotFound();
        }

        (WorkspaceName, Details) = found.Value;
        return Page();
    }
}

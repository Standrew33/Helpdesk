using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Tickets;

public class DetailsModel(TicketService tickets) : PageModel
{
    [FromRoute]
    public int Id { get; set; }

    [TempData]
    public string? Error { get; set; }

    public TicketDetails Details { get; private set; } = null!;

    public bool IsStaff { get; private set; }

    public IReadOnlyList<TicketStatus> AllowedStatuses { get; private set; } = [];

    public IReadOnlyList<AssigneeOption> Assignees { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var actor = User.ToActor();
        var details = await tickets.GetAsync(Id, actor, ct);

        if (details is null)
        {
            return NotFound();
        }

        Details = details;
        IsStaff = actor.Role.IsStaff();
        AllowedStatuses = Enum.GetValues<TicketStatus>()
            .Where(s => s != details.Ticket.Status && TicketWorkflow.IsAllowed(details.Ticket.Status, s, actor.Role))
            .ToList();

        if (IsStaff)
        {
            Assignees = await tickets.GetAssigneeOptionsAsync();
        }

        return Page();
    }

    public Task<IActionResult> OnPostCommentAsync(string? rowVersion, string? body, bool isInternal)
        => RunAsync(actor => tickets.AddCommentAsync(Id, actor, rowVersion, body ?? string.Empty, isInternal));

    public Task<IActionResult> OnPostStatusAsync(string? rowVersion, TicketStatus status)
        => RunAsync(actor => tickets.ChangeStatusAsync(Id, actor, rowVersion, status));

    public Task<IActionResult> OnPostAssignAsync(string? rowVersion, string? assigneeId)
        => RunAsync(actor => tickets.AssignAsync(Id, actor, rowVersion, assigneeId));

    public Task<IActionResult> OnPostPriorityAsync(string? rowVersion, TicketPriority priority)
        => RunAsync(actor => tickets.ChangePriorityAsync(Id, actor, rowVersion, priority));

    private async Task<IActionResult> RunAsync(Func<Actor, Task<ServiceResult>> operation)
    {
        var result = await operation(User.ToActor());

        if (result.Outcome == ServiceOutcome.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            Error = result.Message;
        }

        return RedirectToPage(new { id = Id });
    }
}

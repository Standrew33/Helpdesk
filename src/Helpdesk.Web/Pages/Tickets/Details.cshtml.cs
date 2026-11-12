using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Tickets;

// Slightly above the file limit, so oversized uploads are cut off early and answered with a clear message.
[RequestSizeLimit(AttachmentPolicy.MaxFileBytes + 512 * 1024)]
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

    public async Task<IActionResult> OnPostAttachmentAsync(string? rowVersion, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            Error = "Choose a file to attach.";
            return RedirectToPage(new { id = Id });
        }

        await using var content = file.OpenReadStream();
        return await RunAsync(actor => tickets.AddAttachmentAsync(Id, actor, rowVersion, file.FileName, file.Length, content, ct));
    }

    public async Task<IActionResult> OnGetDownloadAsync(int attachmentId, CancellationToken ct)
    {
        var download = await tickets.OpenAttachmentAsync(Id, attachmentId, User.ToActor(), ct);

        if (download is null)
        {
            return NotFound();
        }

        // Served as a download, never rendered by the browser, whatever the file contains.
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(download.Content, download.ContentType, download.FileName);
    }

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

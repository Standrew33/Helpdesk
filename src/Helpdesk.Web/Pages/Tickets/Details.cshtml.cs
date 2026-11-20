using System.Globalization;
using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;
using Helpdesk.Web.Features.Notifications;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Features.Toasts;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Tickets;

// Slightly above the file limit, so oversized uploads are cut off early and answered with a clear message.
[RequestSizeLimit(AttachmentPolicy.MaxFileBytes + 512 * 1024)]
public class DetailsModel(TicketService tickets, NotificationService notifications) : PageModel
{
    /// <summary>The number of the ticket within the user's workspace.</summary>
    [FromRoute]
    public int Number { get; set; }

    public TicketDetails Details { get; private set; } = null!;

    public bool IsStaff { get; private set; }

    /// <summary>The current status and the ones it may move to; a single entry means the status cannot be changed.</summary>
    public IReadOnlyList<TicketStatus> StatusOptions { get; private set; } = [];

    public bool IsOpen => Details.Ticket.Status != TicketStatus.Closed;

    public PriorityChoice PriorityChoice => new(
        "priority", "Priority", TicketDisplay.PrioritiesFor(IsStaff, Details.Ticket.Priority), Details.Ticket.Priority);

    public IReadOnlyList<AssigneeOption> Assignees { get; private set; } = [];

    /// <summary>Today in UTC, the day due dates are measured against.</summary>
    public DateOnly Today { get; } = Ticket.Today(DateTimeOffset.UtcNow);

    /// <summary>The earliest day the calendar offers: today, or the due date already passed.</summary>
    public DateOnly EarliestDueDate => Details.Ticket.DueDate is { } due && due < Today ? due : Today;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var actor = User.ToActor();
        var details = await tickets.GetAsync(Number, actor, ct);

        if (details is null)
        {
            return NotFound();
        }

        Details = details;
        IsStaff = actor.Role.IsStaff();

        // Seeing the ticket is reading what one was told about it; the bell of this very page already counts it.
        await notifications.MarkTicketReadAsync(actor, details.Ticket.Id, ct);
        StatusOptions = Enum.GetValues<TicketStatus>()
            .Where(s => s == details.Ticket.Status || TicketWorkflow.IsAllowed(details.Ticket.Status, s, actor.Role))
            .ToList();

        if (IsStaff)
        {
            Assignees = await tickets.GetAssigneeOptionsAsync(actor, ct);
        }

        return Page();
    }

    public Task<IActionResult> OnPostCommentAsync(string? rowVersion, string? body, bool isInternal)
        => RunAsync(
            actor => tickets.AddCommentAsync(Number, actor, rowVersion, body ?? string.Empty, isInternal),
            isInternal ? $"Internal note added to #{Number}." : $"Comment added to #{Number}.");

    public async Task<IActionResult> OnPostAttachmentAsync(string? rowVersion, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Refused("Choose a file to attach.");
        }

        await using var content = file.OpenReadStream();
        return await RunAsync(
            actor => tickets.AddAttachmentAsync(Number, actor, rowVersion, file.FileName, file.Length, content, ct),
            $"File attached to #{Number}.");
    }

    public async Task<IActionResult> OnGetDownloadAsync(int attachmentId, CancellationToken ct)
    {
        var download = await tickets.OpenAttachmentAsync(Number, attachmentId, User.ToActor(), ct);

        if (download is null)
        {
            return NotFound();
        }

        // Served as a download, never rendered by the browser, whatever the file contains: attachment disposition,
        // no content sniffing, and a policy that would block every script even if a browser did open it.
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = SecurityHeaders.DownloadContentSecurityPolicy;
        Response.Headers.CacheControl = "private, no-store";
        return File(download.Content, download.ContentType, download.FileName);
    }

    public Task<IActionResult> OnPostStatusAsync(string? rowVersion, TicketStatus status, string? reason)
        => RunAsync(
            actor => tickets.ChangeStatusAsync(Number, actor, rowVersion, status, reason),
            $"Status of #{Number} changed to {TicketDisplay.StatusText(status)}.");

    // An empty field arrives as null; it is passed on as empty so that it is refused rather than left unchanged.
    public Task<IActionResult> OnPostSubjectAsync(string? rowVersion, string? subject)
        => RunAsync(actor => tickets.EditAsync(Number, actor, rowVersion, subject: subject ?? string.Empty), $"Subject of #{Number} saved.");

    public Task<IActionResult> OnPostDescriptionAsync(string? rowVersion, string? description)
        => RunAsync(
            actor => tickets.EditAsync(Number, actor, rowVersion, description: description ?? string.Empty),
            $"Description of #{Number} saved.");

    public async Task<IActionResult> OnPostPriorityAsync(string? rowVersion, TicketPriority? priority)
    {
        if (priority is null)
        {
            return Refused("Choose a priority.");
        }

        return await RunAsync(
            actor => tickets.EditAsync(Number, actor, rowVersion, priority: priority),
            $"Priority of #{Number} changed to {priority}.");
    }

    /// <summary>An empty field removes the due date; text that is not a day is refused rather than taken as empty.</summary>
    public async Task<IActionResult> OnPostDueDateAsync(string? rowVersion, string? dueDate)
    {
        DateOnly? day = null;

        if (!string.IsNullOrWhiteSpace(dueDate))
        {
            if (!DateOnly.TryParseExact(dueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return Refused("Choose the due date in the calendar.");
            }

            day = parsed;
        }

        return await RunAsync(
            actor => tickets.SetDueDateAsync(Number, actor, rowVersion, day),
            day is { } set
                ? $"Due date of #{Number} set to {set.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}."
                : $"Due date of #{Number} removed.");
    }

    public Task<IActionResult> OnPostAssignAsync(string? rowVersion, string? assigneeId)
        => RunAsync(
            actor => tickets.AssignAsync(Number, actor, rowVersion, assigneeId),
            string.IsNullOrEmpty(assigneeId) ? $"#{Number} is no longer assigned." : $"Assignee of #{Number} changed.");

    /// <summary>Runs a change and draws the ticket again, with a message on how it went.</summary>
    private async Task<IActionResult> RunAsync(Func<Actor, Task<ServiceResult>> operation, string done)
    {
        var result = await operation(User.ToActor());

        if (result.Outcome == ServiceOutcome.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            return Refused(result.Message ?? "The change could not be saved.");
        }

        TempData.Success(done);
        return RedirectToPage(new { number = Number, workspaceId = User.VisitedWorkspaceId() });
    }

    private RedirectToPageResult Refused(string message)
    {
        TempData.Failure(message);
        return RedirectToPage(new { number = Number, workspaceId = User.VisitedWorkspaceId() });
    }
}

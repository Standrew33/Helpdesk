using Helpdesk.Core.Tickets;

namespace Helpdesk.Web.Features.Tickets;

public sealed record TicketListItem(
    int Id,
    string Subject,
    TicketStatus Status,
    TicketPriority Priority,
    string CustomerName,
    string? AssigneeName,
    DateTimeOffset DueAt,
    bool IsOverdue);

/// <summary>Narrows the queue for staff; customers always see only their own tickets.</summary>
public enum TicketScope
{
    Mine,
    Unassigned,
    Overdue,
}

public sealed record TicketFilter(
    string? Search = null,
    TicketStatus? Status = null,
    TicketPriority? Priority = null,
    TicketScope? Scope = null,
    int Page = 1);

public sealed record PagedList<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < PageCount;
}

public sealed record AssigneeOption(string Id, string Name);

public sealed record AttachmentDownload(string FileName, string ContentType, Stream Content);

public sealed class TicketDetails
{
    public TicketDetails(Ticket ticket, IReadOnlyList<Comment> comments, IReadOnlyDictionary<string, string> userNames)
    {
        Ticket = ticket;
        Comments = comments;
        UserNames = userNames;
        RowVersion = Convert.ToBase64String(ticket.RowVersion);
    }

    public Ticket Ticket { get; }

    /// <summary>Only the comments the current user may see.</summary>
    public IReadOnlyList<Comment> Comments { get; }

    public IReadOnlyDictionary<string, string> UserNames { get; }

    /// <summary>Opaque version token the page sends back with every change.</summary>
    public string RowVersion { get; }

    public string NameOf(string? userId)
        => userId is not null && UserNames.TryGetValue(userId, out var name) ? name : "-";
}

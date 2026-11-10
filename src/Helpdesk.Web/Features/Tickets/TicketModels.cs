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

public sealed record AssigneeOption(string Id, string Name);

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

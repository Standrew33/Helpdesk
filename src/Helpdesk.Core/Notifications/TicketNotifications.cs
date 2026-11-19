using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;

namespace Helpdesk.Core.Notifications;

/// <summary>One person to tell about a ticket, and what to tell them.</summary>
public sealed record TicketNotice(string RecipientId, NotificationKind Kind, string? Detail = null);

/// <summary>
/// Who hears of a change to a ticket. The customer and the assignee are told what others did; nobody is told of
/// their own doing, and one change makes at most one notification per person: a reply that also moves the status
/// is told as the reply. Internal notes never reach the customer.
/// </summary>
public static class TicketNotifications
{
    /// <summary>What a change is compared against: the ticket as it was just before it.</summary>
    public sealed record Before(TicketStatus Status, string? AssigneeId, int CommentCount);

    public static Before Snapshot(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return new Before(ticket.Status, ticket.AssigneeId, ticket.Comments.Count);
    }

    public static IReadOnlyList<TicketNotice> ForChange(Ticket ticket, Actor actor, Before before)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(before);

        var notices = new List<TicketNotice>();

        // The first thing a person is told about wins; later ones of the same change are left out.
        void Tell(string? recipientId, NotificationKind kind, string? detail = null)
        {
            if (recipientId is not null && recipientId != actor.Id && notices.TrueForAll(n => n.RecipientId != recipientId))
            {
                notices.Add(new TicketNotice(recipientId, kind, detail));
            }
        }

        // The reason a customer gives for closing is told as the closing itself.
        foreach (var comment in ticket.Comments.Skip(before.CommentCount).Where(c => !c.IsClosingReason))
        {
            if (comment.IsInternal)
            {
                Tell(ticket.AssigneeId, NotificationKind.InternalNote);
            }
            else
            {
                Tell(ticket.CustomerId, NotificationKind.Comment);
                Tell(ticket.AssigneeId, NotificationKind.Comment);
            }
        }

        if (ticket.AssigneeId != before.AssigneeId)
        {
            Tell(ticket.AssigneeId, NotificationKind.Assigned);
        }

        if (ticket.Status != before.Status)
        {
            Tell(ticket.CustomerId, NotificationKind.StatusChanged, ticket.Status.ToString());
            Tell(ticket.AssigneeId, NotificationKind.StatusChanged, ticket.Status.ToString());
        }

        return notices;
    }
}

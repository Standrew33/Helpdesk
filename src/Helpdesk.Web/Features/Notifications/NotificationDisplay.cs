using Helpdesk.Core.Notifications;
using Helpdesk.Core.Tickets;
using Helpdesk.Web.Features.Tickets;

namespace Helpdesk.Web.Features.Notifications;

/// <summary>The words of a notification: "Anna replied on #12 Printer is down", "Oleg moved #12 … to Closed".</summary>
public static class NotificationDisplay
{
    /// <summary>What the actor did, said before the ticket.</summary>
    public static string Verb(NotificationKind kind) => kind switch
    {
        NotificationKind.NewTicket => "opened",
        NotificationKind.Assigned => "assigned you",
        NotificationKind.Comment => "replied on",
        NotificationKind.InternalNote => "added an internal note on",
        NotificationKind.StatusChanged => "moved",
        _ => "changed",
    };

    /// <summary>What follows the ticket, such as the new status; empty for most kinds.</summary>
    public static string Ending(NotificationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Kind == NotificationKind.StatusChanged && Enum.TryParse<TicketStatus>(item.Detail, out var status)
            ? $"to {TicketDisplay.StatusText(status)}"
            : string.Empty;
    }

    /// <summary>The count on the bell: up to 99, then "99+".</summary>
    public static string Count(int unread) => unread > 99 ? "99+" : unread.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

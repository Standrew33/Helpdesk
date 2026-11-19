using Helpdesk.Core.Notifications;

namespace Helpdesk.Web.Data;

/// <summary>
/// Something one person is told about a ticket of their workspace: who did it, what kind of thing, and whether it
/// has been read. The text is put together when it is shown, so it follows the ticket's current subject.
/// </summary>
public class Notification
{
    public long Id { get; set; }

    public int WorkspaceId { get; set; }

    public string RecipientId { get; set; } = string.Empty;

    public int TicketId { get; set; }

    /// <summary>Who did what the notification tells about.</summary>
    public string ActorId { get; set; } = string.Empty;

    public NotificationKind Kind { get; set; }

    /// <summary>What it changed to, such as the new status; empty for most kinds.</summary>
    public string? Detail { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}

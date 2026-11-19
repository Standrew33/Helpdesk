namespace Helpdesk.Core.Notifications;

/// <summary>What a notification tells about a ticket.</summary>
public enum NotificationKind
{
    /// <summary>A customer opened a ticket; told to the staff of the workspace.</summary>
    NewTicket = 0,

    /// <summary>The ticket was given to the recipient.</summary>
    Assigned = 1,

    /// <summary>Someone replied in the comments.</summary>
    Comment = 2,

    /// <summary>A member of staff added a note the customer does not see.</summary>
    InternalNote = 3,

    /// <summary>The ticket moved to another status, kept with the notification.</summary>
    StatusChanged = 4,
}

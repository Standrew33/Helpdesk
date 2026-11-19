using Microsoft.AspNetCore.SignalR;

namespace Helpdesk.Web.Features.Notifications;

/// <summary>
/// Tells the open pages of the recipients that a notification is waiting. It carries nothing of the notification
/// itself, and a failure only means the page learns of it later: the change it belongs to is already saved.
/// </summary>
public partial class NotificationPush(IHubContext<NotificationHub> hub, ILogger<NotificationPush> logger)
{
    public async Task TellAsync(IEnumerable<string> recipientIds)
    {
        var ids = recipientIds.Distinct(StringComparer.Ordinal).ToList();

        if (ids.Count == 0)
        {
            return;
        }

        try
        {
            await hub.Clients.Users(ids).SendAsync(NotificationHub.Notified);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            NotSent(logger, ex, ids.Count);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Open pages of {Count} recipients could not be told of a new notification")]
    private static partial void NotSent(ILogger logger, Exception exception, int count);
}

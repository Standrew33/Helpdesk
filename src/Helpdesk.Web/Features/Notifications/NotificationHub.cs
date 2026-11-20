using Helpdesk.Web.Data;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Features.Notifications;

/// <summary>
/// The open line to every page of a signed-in member of a workspace. The server only says that something new is
/// there; the page then reads it like any other page would, with the same checks. While a line is open, its person
/// counts as online; when the last one closes, that moment is kept as when they were last seen.
/// </summary>
[Authorize(Policy = Policies.Workspace)]
public class NotificationHub(Presence presence, HelpdeskDbContext db) : Hub
{
    public const string Path = "/hubs/notifications";

    /// <summary>The message a page gets when a notification for its user was stored.</summary>
    public const string Notified = "notified";

    public override Task OnConnectedAsync()
    {
        if (Context.UserIdentifier is { } id)
        {
            presence.Opened(id);
        }

        return base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.UserIdentifier is { } id && presence.Closed(id))
        {
            var now = DateTimeOffset.UtcNow;
            await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(set => set.SetProperty(u => u.LastSeenAt, now));
        }

        await base.OnDisconnectedAsync(exception);
    }
}

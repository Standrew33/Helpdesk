using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Helpdesk.Web.Features.Notifications;

/// <summary>
/// The open line to every page of a signed-in member of a workspace. The server only says that something new is
/// there; the page then reads it like any other page would, with the same checks.
/// </summary>
[Authorize(Policy = Policies.Workspace)]
public class NotificationHub : Hub
{
    public const string Path = "/hubs/notifications";

    /// <summary>The message a page gets when a notification for its user was stored.</summary>
    public const string Notified = "notified";
}

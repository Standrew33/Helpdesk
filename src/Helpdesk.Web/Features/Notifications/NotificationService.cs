using Helpdesk.Core.Notifications;
using Helpdesk.Core.Users;
using Helpdesk.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Features.Notifications;

/// <summary>One notification as it is shown: who did what to which ticket, and when.</summary>
public sealed record NotificationItem(
    long Id,
    NotificationKind Kind,
    string? Detail,
    int TicketNumber,
    string TicketSubject,
    string ActorName,
    DateTimeOffset CreatedAt,
    bool IsRead);

/// <summary>What a person has been told about the tickets of their workspace, and what of it they have seen.</summary>
public class NotificationService(HelpdeskDbContext db, TimeProvider time)
{
    /// <summary>How many notifications the list shows; older ones are not kept in view.</summary>
    public const int ListSize = 30;

    public Task<int> UnreadCountAsync(Actor actor, CancellationToken ct = default)
        => Of(actor).CountAsync(n => n.ReadAt == null, ct);

    /// <summary>The newest notifications of the actor, read ones too.</summary>
    public async Task<IReadOnlyList<NotificationItem>> RecentAsync(Actor actor, CancellationToken ct = default)
        => await (
            from n in Of(actor)
            join t in db.Tickets on n.TicketId equals t.Id
            join a in db.Users on n.ActorId equals a.Id
            orderby n.CreatedAt descending, n.Id descending
            select new NotificationItem(n.Id, n.Kind, n.Detail, t.Number, t.Subject, a.DisplayName, n.CreatedAt, n.ReadAt != null))
            .Take(ListSize)
            .ToListAsync(ct);

    /// <summary>Opening a ticket counts as reading everything the actor was told about it.</summary>
    public Task MarkTicketReadAsync(Actor actor, int ticketId, CancellationToken ct = default)
        => MarkReadAsync(Of(actor).Where(n => n.TicketId == ticketId), ct);

    public Task MarkAllReadAsync(Actor actor, CancellationToken ct = default)
        => MarkReadAsync(Of(actor), ct);

    private async Task MarkReadAsync(IQueryable<Notification> notifications, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await notifications.Where(n => n.ReadAt == null).ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now), ct);
    }

    private IQueryable<Notification> Of(Actor actor)
        => db.Notifications.Where(n => n.RecipientId == actor.Id && n.WorkspaceId == actor.WorkspaceId);
}

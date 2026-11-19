using Helpdesk.Web.Features.Notifications;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages;

/// <summary>Everything the signed-in person was told lately; the bell in the sidebar shows the same list.</summary>
public class NotificationsModel(NotificationService notifications) : PageModel
{
    public IReadOnlyList<NotificationItem> Items { get; private set; } = [];

    public int Unread { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var actor = User.ToActor();
        Items = await notifications.RecentAsync(actor, ct);
        Unread = Items.Count(i => !i.IsRead);
    }

    public async Task<IActionResult> OnPostReadAllAsync(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(User.ToActor(), ct);
        return RedirectToPage();
    }
}

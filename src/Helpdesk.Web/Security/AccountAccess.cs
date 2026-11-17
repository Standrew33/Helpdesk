using Helpdesk.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Security;

/// <summary>
/// Whether an account may be used at all: it is switched on, and its help desk is not blocked. Every way in asks
/// here, and so does every request of a signed-in user (see <see cref="AppSignInManager"/>).
/// </summary>
public class AccountAccess(HelpdeskDbContext db)
{
    public async Task<bool> AllowsAsync(ApplicationUser? user, CancellationToken ct = default)
        => user is { IsActive: true }
            && (user.WorkspaceId is not { } workspaceId || !await db.Workspaces.AnyAsync(w => w.Id == workspaceId && w.IsBlocked, ct));
}

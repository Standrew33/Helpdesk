using Helpdesk.Core.Users;
using Helpdesk.Core.Workspaces;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Tickets;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Features.Workspaces;

public sealed record JoinLink(string Url, bool IsOpen);

/// <summary>A help desk whose public link works, found by the code in that link.</summary>
public sealed record OpenJoin(int WorkspaceId, string WorkspaceName, string Code);

/// <summary>
/// The public link of a help desk: anyone who has it can sign up as a customer there, the way a company puts
/// "contact support" on its site. Administrators turn it off or replace it; staff can pass it on.
/// </summary>
public class JoinLinkService(HelpdeskDbContext db)
{
    public static string PathFor(string code) => $"/Join/{code}";

    public async Task<JoinLink> GetAsync(Actor viewer, string baseUrl, CancellationToken ct = default)
    {
        var workspace = await db.Workspaces.AsNoTracking().SingleAsync(w => w.Id == viewer.WorkspaceId, ct);
        return new JoinLink(baseUrl + PathFor(workspace.JoinCode), workspace.IsJoinOpen);
    }

    public Task<ServiceResult> SetOpenAsync(Actor admin, bool open, CancellationToken ct = default)
        => ChangeAsync(admin, workspace => workspace.SetJoinOpen(open), ct);

    /// <summary>Gives the help desk a new link; the old one stops working at once, for example after it was shared too widely.</summary>
    public Task<ServiceResult> RenewAsync(Actor admin, CancellationToken ct = default)
        => ChangeAsync(admin, workspace => workspace.RenewJoinCode(), ct);

    public async Task<OpenJoin?> FindOpenAsync(string? code, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(code) || code.Length != Workspace.JoinCodeLength)
        {
            return null;
        }

        return await db.Workspaces
            .Where(w => w.JoinCode == code && w.IsJoinOpen)
            .Select(w => new OpenJoin(w.Id, w.Name, w.JoinCode))
            .FirstOrDefaultAsync(ct);
    }

    private async Task<ServiceResult> ChangeAsync(Actor admin, Action<Workspace> change, CancellationToken ct)
    {
        if (admin.Role != UserRole.Admin || admin.IsDemo)
        {
            return ServiceResult.Invalid("Only the administrators of this help desk can change its link.");
        }

        var workspace = await db.Workspaces.SingleAsync(w => w.Id == admin.WorkspaceId, ct);
        change(workspace);
        await db.SaveChangesAsync(ct);
        return ServiceResult.Success();
    }
}

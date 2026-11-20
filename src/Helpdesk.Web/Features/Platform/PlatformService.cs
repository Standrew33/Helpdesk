using Helpdesk.Core.Tickets;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Attachments;
using Helpdesk.Web.Features.Notifications;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Features.Users;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Features.Platform;

public sealed record WorkspaceRow(int Id, string Name, DateTimeOffset CreatedAt, int People, int Tickets, int OpenTickets, DateTimeOffset? LastActivity, bool IsBlocked);

public sealed record PersonRow(
    string Id, string DisplayName, string Email, string Role, bool IsActive, bool IsOwner, DateTimeOffset? SignedUpAt, DateTimeOffset? LastSignInAt, Seen Seen);

public sealed record WorkspaceTicketRow(int Number, string Subject, TicketStatus Status, TicketPriority Priority, string CustomerName, DateTimeOffset UpdatedAt);

public sealed record WorkspaceOverview(int Id, string Name, DateTimeOffset CreatedAt, bool IsBlocked, IReadOnlyList<PersonRow> People, IReadOnlyList<WorkspaceTicketRow> Tickets);

/// <summary>
/// What the owner of the platform sees and does: every help desk, its people and its tickets, read as they are,
/// and the switch that blocks a help desk or a person. The owner never writes into a ticket.
/// </summary>
public partial class PlatformService(
    HelpdeskDbContext db,
    UserManager<ApplicationUser> userManager,
    Microsoft.Extensions.Options.IOptions<PlatformOptions> platform,
    AccountDeletion deletion,
    Presence presence,
    IAttachmentStore attachments,
    ILogger<PlatformService> logger)
{
    /// <summary>The newest tickets of a help desk the overview shows; older ones are reached by number.</summary>
    public const int MaxTicketsShown = 100;

    private static readonly string[] RoleOrder = [RoleNames.Admin, RoleNames.Operator, RoleNames.Customer];

    public async Task<IReadOnlyList<WorkspaceRow>> ListWorkspacesAsync(CancellationToken ct = default)
    {
        // Help desks are reached across every workspace on purpose: this is the one place made for it.
        var tickets = db.Tickets.IgnoreQueryFilters();

        return await db.Workspaces
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new WorkspaceRow(
                w.Id,
                w.Name,
                w.CreatedAt,
                db.Users.Count(u => u.WorkspaceId == w.Id && u.DeletedAt == null),
                tickets.Count(t => t.WorkspaceId == w.Id),
                tickets.Count(t => t.WorkspaceId == w.Id && t.Status != TicketStatus.Closed),
                tickets.Where(t => t.WorkspaceId == w.Id).Max(t => (DateTimeOffset?)t.UpdatedAt),
                w.IsBlocked))
            .ToListAsync(ct);
    }

    public async Task<WorkspaceOverview?> GetWorkspaceAsync(int id, CancellationToken ct = default)
    {
        var workspace = await db.Workspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);

        if (workspace is null)
        {
            return null;
        }

        var users = await db.Users.AsNoTracking().Where(u => u.WorkspaceId == id && u.DeletedAt == null).ToListAsync(ct);
        var ids = users.Select(u => u.Id).ToList();
        var roles = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            where ids.Contains(ur.UserId)
            select new { ur.UserId, r.Name })
            .ToListAsync(ct);

        var people = users
            .Select(u => new PersonRow(
                u.Id,
                u.DisplayName,
                u.Email ?? string.Empty,
                RoleOrder.FirstOrDefault(r => roles.Any(x => x.UserId == u.Id && x.Name == r)) ?? "-",
                u.IsActive,
                platform.Value.IsOwner(u.Email),
                u.CreatedAt,
                u.LastSignInAt,
                new Seen(presence.IsOnline(u.Id), u.LastSeenAt)))
            .OrderBy(p => Array.IndexOf(RoleOrder, p.Role) is var i and >= 0 ? i : RoleOrder.Length)
            .ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var tickets = await (
            from t in db.Tickets.IgnoreQueryFilters()
            where t.WorkspaceId == id
            join c in db.Users on t.CustomerId equals c.Id
            orderby t.UpdatedAt descending
            select new WorkspaceTicketRow(t.Number, t.Subject, t.Status, t.Priority, c.DisplayName, t.UpdatedAt))
            .Take(MaxTicketsShown)
            .ToListAsync(ct);

        return new WorkspaceOverview(workspace.Id, workspace.Name, workspace.CreatedAt, workspace.IsBlocked, people, tickets);
    }

    /// <summary>A ticket of any help desk, with everything in it, internal notes included.</summary>
    public async Task<(string WorkspaceName, TicketDetails Details)?> GetTicketAsync(int workspaceId, int number, CancellationToken ct = default)
    {
        var workspaceName = await db.Workspaces.Where(w => w.Id == workspaceId).Select(w => w.Name).FirstOrDefaultAsync(ct);
        var ticket = await db.Tickets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(t => t.Comments)
            .Include(t => t.Attachments)
            .Include(t => t.History)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.WorkspaceId == workspaceId && t.Number == number, ct);

        if (workspaceName is null || ticket is null)
        {
            return null;
        }

        var userIds = new HashSet<string> { ticket.CustomerId };

        if (ticket.AssigneeId is not null)
        {
            userIds.Add(ticket.AssigneeId);
        }

        userIds.UnionWith(ticket.Comments.Select(c => c.AuthorId));
        userIds.UnionWith(ticket.Attachments.Select(a => a.UploadedById));
        userIds.UnionWith(ticket.History.Select(h => h.ActorId));
        userIds.UnionWith(ticket.History.Where(h => h.Field == "Assignee").SelectMany(h => new[] { h.OldValue, h.NewValue }).OfType<string>());

        var names = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var comments = ticket.Comments.OrderBy(c => c.CreatedAt).ToList();
        return (workspaceName, new TicketDetails(ticket, comments, names));
    }

    /// <summary>Blocks a help desk or lets it work again; its people are signed out at their next request.</summary>
    public async Task<ServiceResult> SetWorkspaceBlockedAsync(string ownerId, int id, bool blocked, CancellationToken ct = default)
    {
        var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == id, ct);

        if (workspace is null)
        {
            return ServiceResult.NotFound();
        }

        workspace.SetBlocked(blocked);
        await db.SaveChangesAsync(ct);
        SecurityLog.WorkspaceBlocked(logger, ownerId, id, blocked);
        return ServiceResult.Success();
    }

    /// <summary>Switches any account off or on, administrators included; only the owner's own account is out of reach.</summary>
    public async Task<ServiceResult> SetUserActiveAsync(string ownerId, string userId, bool active)
    {
        var user = await userManager.FindByIdAsync(userId);

        if (user is not { DeletedAt: null })
        {
            return ServiceResult.NotFound();
        }

        if (user.Id == ownerId || platform.Value.IsOwner(user.Email))
        {
            return ServiceResult.Invalid("The owner of the platform cannot be switched off.");
        }

        user.IsActive = active;

        if (!(await userManager.UpdateAsync(user)).Succeeded)
        {
            return ServiceResult.Invalid("The account could not be updated.");
        }

        await userManager.UpdateSecurityStampAsync(user);
        SecurityLog.ActiveChanged(logger, ownerId, user.Id, active);
        return ServiceResult.Success();
    }

    /// <summary>Deletes anyone for good, administrators included; what they wrote stays, signed "Deleted user".</summary>
    public async Task<ServiceResult> DeleteUserAsync(string ownerId, string userId)
    {
        var user = await userManager.FindByIdAsync(userId);

        if (user is not { DeletedAt: null })
        {
            return ServiceResult.NotFound();
        }

        if (user.Id == ownerId || platform.Value.IsOwner(user.Email))
        {
            return ServiceResult.Invalid("The owner of the platform cannot be deleted.");
        }

        if (!await deletion.DeleteAsync(user))
        {
            return ServiceResult.Invalid("The account could not be deleted.");
        }

        SecurityLog.AccountDeleted(logger, ownerId, user.Id);
        return ServiceResult.Success();
    }

    /// <summary>
    /// Deletes a help desk with everything in it: its tickets with their comments, files and history, its invitations
    /// and its people. The database part happens at once or not at all; the stored files are removed afterwards.
    /// The name must be typed exactly, so a slip of the mouse cannot do it.
    /// </summary>
    public async Task<ServiceResult> DeleteWorkspaceAsync(string ownerId, int id, string? confirmName, CancellationToken ct = default)
    {
        var workspace = await db.Workspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);

        if (workspace is null)
        {
            return ServiceResult.NotFound();
        }

        if (!string.Equals(confirmName?.Trim(), workspace.Name, StringComparison.Ordinal))
        {
            return ServiceResult.Invalid("Type the name of the workspace exactly as it is shown to delete it.");
        }

        var tickets = db.Tickets.IgnoreQueryFilters().Where(t => t.WorkspaceId == id);
        var files = await tickets.SelectMany(t => t.Attachments).Select(a => a.StoredName).ToListAsync(ct);
        var ticketCount = await tickets.CountAsync(ct);
        var people = await db.Users.CountAsync(u => u.WorkspaceId == id && u.DeletedAt == null, ct);
        var ownerEmail = userManager.NormalizeEmail(platform.Value.OwnerEmail);

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // Comments, files and history go with their tickets (the database cascades them).
            await tickets.ExecuteDeleteAsync(ct);
            await db.Invitations.Where(i => i.WorkspaceId == id).ExecuteDeleteAsync(ct);

            // Should the owner of the platform have joined this help desk, the owner leaves it instead of going with it.
            await db.Users
                .Where(u => u.WorkspaceId == id && u.NormalizedEmail == ownerEmail)
                .ExecuteUpdateAsync(set => set.SetProperty(u => u.WorkspaceId, (int?)null), ct);
            await db.Users.Where(u => u.WorkspaceId == id).ExecuteDeleteAsync(ct);
            await db.Workspaces.Where(w => w.Id == id).ExecuteDeleteAsync(ct);

            await transaction.CommitAsync(ct);
        });

        foreach (var file in files)
        {
            try
            {
                await attachments.DeleteAsync(file, CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                FileLeftBehind(logger, ex, file);
            }
        }

        SecurityLog.WorkspaceDeleted(logger, ownerId, id, people, ticketCount);
        return ServiceResult.Success();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The stored file {StoredName} of a deleted help desk could not be removed")]
    private static partial void FileLeftBehind(ILogger logger, Exception exception, string storedName);
}

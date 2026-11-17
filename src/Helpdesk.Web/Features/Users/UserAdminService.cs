using Helpdesk.Core.Users;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Features.Users;

/// <param name="ProtectedAs">Why the account cannot be changed here, if it cannot.</param>
public sealed record UserRow(string Id, string DisplayName, string Email, string Role, bool IsActive, bool IsSelf, string? ProtectedAs)
{
    public bool IsProtected => ProtectedAs is not null;
}

/// <summary>
/// Lets an administrator see the users of their own workspace, change their role and switch accounts on or off.
/// People of other workspaces are out of reach: they are not listed, and asking for one of them finds nobody.
/// </summary>
public class UserAdminService(
    HelpdeskDbContext db,
    UserManager<ApplicationUser> userManager,
    IOptions<PlatformOptions> platform,
    AccountDeletion deletion,
    ILogger<UserAdminService> logger)
{
    public const int MaxListSize = 100;

    private const string OwnerAccount = "Owner of the platform";

    // Roles ordered from the most to the least powerful; a user shows as the first one they have.
    private static readonly string[] RoleOrder = [RoleNames.Admin, RoleNames.Operator, RoleNames.Customer];

    public async Task<IReadOnlyList<UserRow>> ListAsync(Actor admin, string? search, CancellationToken ct = default)
    {
        var users = db.Users.AsNoTracking().Where(u => u.WorkspaceId == admin.WorkspaceId && u.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            users = users.Where(u => u.DisplayName.Contains(term) || u.Email!.Contains(term));
        }

        var page = await users.OrderBy(u => u.DisplayName).ThenBy(u => u.Email).Take(MaxListSize).ToListAsync(ct);
        var ids = page.Select(u => u.Id).ToList();

        var assignments = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            where ids.Contains(ur.UserId)
            select new { ur.UserId, RoleName = r.Name })
            .ToListAsync(ct);

        var rolesByUser = assignments
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => RoleOrder.FirstOrDefault(r => g.Any(a => a.RoleName == r)) ?? "-");

        return page
            .Select(u => new UserRow(
                u.Id,
                u.DisplayName,
                u.Email ?? string.Empty,
                rolesByUser.GetValueOrDefault(u.Id, "-"),
                u.IsActive,
                IsSelf: u.Id == admin.Id,
                ProtectedAs: ProtectedAs(u)))
            .OrderBy(r => Array.IndexOf(RoleOrder, r.Role) is var i and >= 0 ? i : RoleOrder.Length)
            .ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<ServiceResult> ChangeRoleAsync(Actor admin, string userId, string role)
    {
        if (!RoleOrder.Contains(role))
        {
            return ServiceResult.Invalid("Unknown role.");
        }

        var user = await FindInWorkspaceAsync(admin, userId);

        if (user is null)
        {
            return ServiceResult.NotFound();
        }

        if (user.Id == admin.Id)
        {
            return ServiceResult.Invalid("You cannot change your own role. Ask another administrator.");
        }

        if (ProtectedAs(user) is not null)
        {
            return ServiceResult.Invalid("The owner of the platform cannot be changed here.");
        }

        var current = await userManager.GetRolesAsync(user);

        if (current.Count == 1 && current[0] == role)
        {
            return ServiceResult.Success();
        }

        var removed = await userManager.RemoveFromRolesAsync(user, current);
        var added = removed.Succeeded ? await userManager.AddToRoleAsync(user, role) : removed;

        if (!added.Succeeded)
        {
            return ServiceResult.Invalid("The role could not be changed.");
        }

        // The role is part of the sign-in cookie; a new stamp makes the old cookie invalid, so the change applies at once.
        await userManager.UpdateSecurityStampAsync(user);
        SecurityLog.RoleChanged(logger, admin.Id, user.Id, role);
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SetActiveAsync(Actor admin, string userId, bool active)
    {
        var user = await FindInWorkspaceAsync(admin, userId);

        if (user is null)
        {
            return ServiceResult.NotFound();
        }

        if (user.Id == admin.Id)
        {
            return ServiceResult.Invalid("You cannot deactivate your own account.");
        }

        if (ProtectedAs(user) is not null)
        {
            return ServiceResult.Invalid("The owner of the platform cannot be changed here.");
        }

        user.IsActive = active;

        if (!(await userManager.UpdateAsync(user)).Succeeded)
        {
            return ServiceResult.Invalid("The account could not be updated.");
        }

        await userManager.UpdateSecurityStampAsync(user);
        SecurityLog.ActiveChanged(logger, admin.Id, user.Id, active);
        return ServiceResult.Success();
    }

    /// <summary>Deletes a person of the own help desk for good; what they wrote stays, signed "Deleted user".</summary>
    public async Task<ServiceResult> DeleteAsync(Actor admin, string userId)
    {
        var user = await FindInWorkspaceAsync(admin, userId);

        if (user is null)
        {
            return ServiceResult.NotFound();
        }

        if (user.Id == admin.Id)
        {
            return ServiceResult.Invalid("You cannot delete your own account. Ask another administrator.");
        }

        if (ProtectedAs(user) is not null)
        {
            return ServiceResult.Invalid("The owner of the platform cannot be changed here.");
        }

        if (!await deletion.DeleteAsync(user))
        {
            return ServiceResult.Invalid("The account could not be deleted.");
        }

        SecurityLog.AccountDeleted(logger, admin.Id, user.Id);
        return ServiceResult.Success();
    }

    /// <summary>A user of another workspace, or a deleted one, looks the same as one who does not exist.</summary>
    private async Task<ApplicationUser?> FindInWorkspaceAsync(Actor admin, string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        return user is { DeletedAt: null } && user.WorkspaceId == admin.WorkspaceId ? user : null;
    }

    /// <summary>The owner of the platform is out of reach of the administrators of a help desk.</summary>
    private string? ProtectedAs(ApplicationUser user) => platform.Value.IsOwner(user.Email) ? OwnerAccount : null;
}

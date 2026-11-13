using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Features.Users;

public sealed record UserRow(string Id, string DisplayName, string Email, string Role, bool IsActive, bool IsSelf, bool IsProtected);

/// <summary>Lets an administrator see the users, change their role and switch accounts on or off.</summary>
public class UserAdminService(HelpdeskDbContext db, UserManager<ApplicationUser> userManager, IOptions<DemoOptions> demo)
{
    public const int MaxListSize = 100;

    // Roles ordered from the most to the least powerful; a user shows as the first one they have.
    private static readonly string[] RoleOrder = [RoleNames.Admin, RoleNames.Operator, RoleNames.Customer];

    public async Task<IReadOnlyList<UserRow>> ListAsync(string currentUserId, string? search, CancellationToken ct = default)
    {
        var users = db.Users.AsNoTracking();

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
                IsSelf: u.Id == currentUserId,
                IsProtected: IsProtected(u)))
            .OrderBy(r => Array.IndexOf(RoleOrder, r.Role) is var i and >= 0 ? i : RoleOrder.Length)
            .ThenBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<ServiceResult> ChangeRoleAsync(string currentUserId, string userId, string role)
    {
        if (!RoleOrder.Contains(role))
        {
            return ServiceResult.Invalid("Unknown role.");
        }

        var user = await userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return ServiceResult.NotFound();
        }

        if (user.Id == currentUserId)
        {
            return ServiceResult.Invalid("You cannot change your own role. Ask another administrator.");
        }

        if (IsProtected(user))
        {
            return ServiceResult.Invalid("The demo accounts cannot be changed.");
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
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> SetActiveAsync(string currentUserId, string userId, bool active)
    {
        var user = await userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return ServiceResult.NotFound();
        }

        if (user.Id == currentUserId)
        {
            return ServiceResult.Invalid("You cannot deactivate your own account.");
        }

        if (IsProtected(user))
        {
            return ServiceResult.Invalid("The demo accounts cannot be changed.");
        }

        user.IsActive = active;

        if (!(await userManager.UpdateAsync(user)).Succeeded)
        {
            return ServiceResult.Invalid("The account could not be updated.");
        }

        await userManager.UpdateSecurityStampAsync(user);
        return ServiceResult.Success();
    }

    /// <summary>The public demo must keep working for the next visitor, so its accounts are read-only.</summary>
    private bool IsProtected(ApplicationUser user) => demo.Value.Enabled && DatabaseSeeder.IsDemoAddress(user.Email);
}

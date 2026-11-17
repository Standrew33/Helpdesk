using Helpdesk.Core.Workspaces;
using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace Helpdesk.Web.Features.Workspaces;

/// <summary>
/// Someone who signs up by themselves starts a help desk of their own and becomes its administrator.
/// Either all of it is created, or nothing is left behind.
/// </summary>
public class WorkspaceSignUp(HelpdeskDbContext db, UserManager<ApplicationUser> userManager, TimeProvider time)
{
    /// <summary>The name a help desk gets when none was given.</summary>
    public static string DefaultName(string displayName) => $"{displayName}'s help desk";

    /// <summary>
    /// Creates the account, its workspace and the administrator role. A name that breaks the rules throws
    /// <see cref="Core.DomainException"/> before anything is saved; refusals of the account itself are returned.
    /// </summary>
    public async Task<IdentityResult> CreateAsync(ApplicationUser user, string? password, string workspaceName)
    {
        ArgumentNullException.ThrowIfNull(user);

        var workspace = Workspace.Create(workspaceName, time.GetUtcNow());

        // The account goes first: a weak password or a taken address is refused before a workspace exists.
        var created = string.IsNullOrEmpty(password)
            ? await userManager.CreateAsync(user)
            : await userManager.CreateAsync(user, password);

        if (!created.Succeeded)
        {
            return created;
        }

        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();

        user.WorkspaceId = workspace.Id;
        var joined = await userManager.UpdateAsync(user);
        var inRole = joined.Succeeded ? await userManager.AddToRoleAsync(user, RoleNames.Admin) : joined;

        if (!inRole.Succeeded)
        {
            await userManager.DeleteAsync(user);
            db.Workspaces.Remove(workspace);
            await db.SaveChangesAsync();
        }

        return inRole;
    }

    /// <summary>Creates the account of someone who came through the public link of a help desk, as its customer.</summary>
    public async Task<IdentityResult> JoinAsCustomerAsync(ApplicationUser user, string? password, int workspaceId)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.WorkspaceId = workspaceId;

        var created = string.IsNullOrEmpty(password)
            ? await userManager.CreateAsync(user)
            : await userManager.CreateAsync(user, password);

        if (!created.Succeeded)
        {
            return created;
        }

        var inRole = await userManager.AddToRoleAsync(user, RoleNames.Customer);

        if (!inRole.Succeeded)
        {
            await userManager.DeleteAsync(user);
        }

        return inRole;
    }
}

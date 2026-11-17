using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Features.Users;

/// <summary>
/// Deletes a person for good. Tickets, comments and files they left keep an author, shown as "Deleted user";
/// the name, address, password and roles are erased, and the address is free to sign up again.
/// </summary>
public class AccountDeletion(HelpdeskDbContext db, UserManager<ApplicationUser> userManager, TimeProvider time)
{
    public const string DeletedName = "Deleted user";

    public async Task<bool> DeleteAsync(ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var oldEmail = user.NormalizedEmail;
        var placeholder = $"deleted-{user.Id}@deleted.invalid";

        var roles = await userManager.GetRolesAsync(user);
        var steps = new List<IdentityResult>
        {
            await userManager.RemoveFromRolesAsync(user, roles),
            await userManager.SetUserNameAsync(user, placeholder),
            await userManager.SetEmailAsync(user, placeholder),
        };

        if (await userManager.HasPasswordAsync(user))
        {
            steps.Add(await userManager.RemovePasswordAsync(user));
        }

        user.DisplayName = DeletedName;
        user.EmailConfirmed = false;
        user.PhoneNumber = null;
        user.IsActive = false;
        user.DeletedAt = time.GetUtcNow();
        steps.Add(await userManager.UpdateAsync(user));

        // A new stamp ends every session at once; open sign-in codes for the old address are dropped.
        steps.Add(await userManager.UpdateSecurityStampAsync(user));
        await db.SignInChallenges.Where(c => c.Email == oldEmail).ExecuteDeleteAsync();

        return steps.All(s => s.Succeeded);
    }
}

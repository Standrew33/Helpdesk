using System.Security.Claims;
using Helpdesk.Core.Users;
using Helpdesk.Web.Data;

namespace Helpdesk.Web.Security;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The name to show for the signed-in user; falls back to the sign-in name for older cookies.</summary>
    public static string DisplayName(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.FindFirstValue(AppClaimTypes.DisplayName) ?? principal.Identity?.Name ?? string.Empty;
    }

    /// <summary>Maps the signed-in user to the actor the domain works with.</summary>
    public static Actor ToActor(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The user is not signed in.");

        // A user normally has one role; if there are several, the most powerful one wins.
        var role = principal.IsInRole(RoleNames.Admin) ? UserRole.Admin
            : principal.IsInRole(RoleNames.Operator) ? UserRole.Operator
            : principal.IsInRole(RoleNames.Customer) ? UserRole.Customer
            : throw new InvalidOperationException("The user has no role.");

        return new Actor(id, role);
    }
}

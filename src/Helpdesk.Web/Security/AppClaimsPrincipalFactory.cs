using System.Globalization;
using System.Security.Claims;
using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Security;

public static class AppClaimTypes
{
    public const string DisplayName = "display_name";

    /// <summary>Present ("true") for the shared demo accounts.</summary>
    public const string Demo = "is_demo";

    /// <summary>The help desk the user belongs to.</summary>
    public const string Workspace = "workspace_id";
}

/// <summary>Adds the display name to the sign-in cookie, so pages can greet the user by name instead of by e-mail.</summary>
public sealed class AppClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(AppClaimTypes.DisplayName, user.DisplayName));

        // The cookie is checked against the database on every request and built anew (see SecurityStampValidatorOptions),
        // so this always names the workspace the user belongs to now.
        if (user.WorkspaceId is { } workspaceId)
        {
            identity.AddClaim(new Claim(AppClaimTypes.Workspace, workspaceId.ToString(CultureInfo.InvariantCulture)));
        }

        if (DatabaseSeeder.IsDemoAddress(user.Email))
        {
            identity.AddClaim(new Claim(AppClaimTypes.Demo, "true"));
        }

        return identity;
    }
}

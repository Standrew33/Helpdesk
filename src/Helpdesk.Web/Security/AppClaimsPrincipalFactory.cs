using System.Globalization;
using System.Security.Claims;
using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Security;

public static class AppClaimTypes
{
    public const string DisplayName = "display_name";

    /// <summary>The help desk the user belongs to.</summary>
    public const string Workspace = "workspace_id";

    /// <summary>Present ("true") for the owner of the platform, the superadmin.</summary>
    public const string PlatformOwner = "platform_owner";

    /// <summary>
    /// Present ("true") while the owner of the platform works in a help desk through its pages under /Platform;
    /// never stored in the cookie (see <see cref="PlatformVisit"/>).
    /// </summary>
    public const string Visiting = "visiting";
}

/// <summary>Adds the display name to the sign-in cookie, so pages can greet the user by name instead of by e-mail.</summary>
public sealed class AppClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options,
    IOptions<PlatformOptions> platform)
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

        // Only a proven address counts: the configured owner address must really be the owner's mailbox.
        if (user.EmailConfirmed && platform.Value.IsOwner(user.Email))
        {
            identity.AddClaim(new Claim(AppClaimTypes.PlatformOwner, "true"));
        }

        return identity;
    }
}

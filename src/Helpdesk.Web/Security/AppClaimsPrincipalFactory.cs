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

        if (DatabaseSeeder.IsDemoAddress(user.Email))
        {
            identity.AddClaim(new Claim(AppClaimTypes.Demo, "true"));
        }

        return identity;
    }
}

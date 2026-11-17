using System.Security.Claims;
using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Security;

/// <summary>
/// The sign-in cookie is checked against the database on every request. Besides the security stamp, the check asks
/// whether the account may still be used, so a deactivated user or a blocked help desk is signed out at once.
/// </summary>
public sealed class AppSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation,
    AccountAccess access)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<ApplicationUser?> ValidateSecurityStampAsync(ClaimsPrincipal? principal)
    {
        var user = await base.ValidateSecurityStampAsync(principal);
        return await access.AllowsAsync(user) ? user : null;
    }
}

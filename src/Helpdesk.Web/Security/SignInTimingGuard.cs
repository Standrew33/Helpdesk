using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Security;

/// <summary>
/// Checking a password takes noticeable time on purpose. When the address is unknown there is nothing to check,
/// so the answer would come back faster and tell an attacker which addresses have an account. This does the same
/// amount of work for addresses without a usable account.
/// </summary>
public sealed class SignInTimingGuard(IOptions<PasswordHasherOptions> options)
{
    private readonly PasswordHasher<ApplicationUser> _hasher = new(options);

    private readonly Lazy<string> _decoyHash = new(() => new PasswordHasher<ApplicationUser>(options)
        .HashPassword(new ApplicationUser(), Guid.NewGuid().ToString("N")));

    public void SpendTheSameTime(string password)
        => _hasher.VerifyHashedPassword(new ApplicationUser(), _decoyHash.Value, password);
}

using System.Text.Json;
using Helpdesk.Web.Data;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Features.SignIn;

public enum EmailSignInResult
{
    SignedIn,

    /// <summary>The address is proven, but there is no account yet: the person chooses a name next.</summary>
    NeedsProfile,
    Deactivated,
}

/// <summary>What happens once someone has proven an e-mail address with a code or link.</summary>
public class EmailSignIn(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IDataProtectionProvider dataProtection,
    IOptions<AppCookieOptions> cookies,
    IOptions<OneTimeCodeOptions> codes,
    ILogger<EmailSignIn> logger)
{
    private const string PendingPurpose = "Helpdesk.PendingSignUp";

    /// <summary>How long a proven address waits for the person to finish creating the account.</summary>
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(15);

    private string PendingCookie => cookies.Value.Prefix + "Helpdesk.SignUp";

    private ITimeLimitedDataProtector Protector => dataProtection.CreateProtector(PendingPurpose).ToTimeLimitedDataProtector();

    public TimeSpan CodeLifetime => codes.Value.Lifetime;

    public async Task<EmailSignInResult> CompleteAsync(HttpContext context, string email, string? returnUrl)
    {
        ArgumentNullException.ThrowIfNull(context);
        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            RememberPendingSignUp(context, email.ToLowerInvariant(), returnUrl);
            return EmailSignInResult.NeedsProfile;
        }

        if (!user.IsActive)
        {
            SecurityLog.SignInFailed(logger, user.Id);
            return EmailSignInResult.Deactivated;
        }

        // The code reached the mailbox, which proves the address belongs to this person.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        SecurityLog.SignedIn(logger, user.Id);
        return EmailSignInResult.SignedIn;
    }

    /// <summary>The address proven a moment ago in this browser, waiting for a name; null when there is none or it is too old.</summary>
    public PendingSignUp? ReadPendingSignUp(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Cookies.TryGetValue(PendingCookie, out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PendingSignUp>(Protector.Unprotect(value));
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException)
        {
            return null;
        }
    }

    public void ForgetPendingSignUp(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.Cookies.Delete(PendingCookie, CookieOptions());
    }

    private void RememberPendingSignUp(HttpContext context, string email, string? returnUrl)
    {
        // Signed, encrypted and time-limited by data protection: the browser can carry it but cannot read or change it.
        var value = Protector.Protect(JsonSerializer.Serialize(new PendingSignUp(email, returnUrl)), PendingLifetime);
        context.Response.Cookies.Append(PendingCookie, value, CookieOptions());
    }

    private CookieOptions CookieOptions() => new()
    {
        HttpOnly = true,
        Secure = cookies.Value.SecurePolicy == CookieSecurePolicy.Always,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = PendingLifetime,
        IsEssential = true,
    };
}

public sealed record PendingSignUp(string Email, string? ReturnUrl);

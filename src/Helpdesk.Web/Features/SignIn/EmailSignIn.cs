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

    /// <summary>The address is proven for a password reset: the person chooses a new password next.</summary>
    PasswordResetReady,
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
    /// <summary>How long a proven address waits for the person to finish the next step.</summary>
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(15);

    private static readonly PendingStep SignUpStep = new("Helpdesk.SignUp", "Helpdesk.PendingSignUp");
    private static readonly PendingStep ResetStep = new("Helpdesk.Reset", "Helpdesk.PendingReset");

    public TimeSpan CodeLifetime => codes.Value.Lifetime;

    /// <summary>Signs in the owner of a proven address, or prepares the welcome step when there is no account.</summary>
    public async Task<EmailSignInResult> CompleteAsync(HttpContext context, string email, string? returnUrl)
    {
        ArgumentNullException.ThrowIfNull(context);
        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            Remember(context, SignUpStep, new PendingSignUp(email.ToLowerInvariant(), returnUrl));
            return EmailSignInResult.NeedsProfile;
        }

        if (!await CanUseAsync(user))
        {
            return EmailSignInResult.Deactivated;
        }

        await signInManager.SignInAsync(user, isPersistent: false);
        SecurityLog.SignedIn(logger, user.Id);
        return EmailSignInResult.SignedIn;
    }

    /// <summary>
    /// For a proven reset request: prepares the new-password step. The person is not signed in yet; that happens
    /// once the new password is set. An address without an account goes to the welcome step instead.
    /// </summary>
    public async Task<EmailSignInResult> BeginPasswordResetAsync(HttpContext context, string email)
    {
        ArgumentNullException.ThrowIfNull(context);
        var user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            Remember(context, SignUpStep, new PendingSignUp(email.ToLowerInvariant(), null));
            return EmailSignInResult.NeedsProfile;
        }

        if (!await CanUseAsync(user))
        {
            return EmailSignInResult.Deactivated;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        Remember(context, ResetStep, new PendingReset(user.Id, token));
        return EmailSignInResult.PasswordResetReady;
    }

    /// <summary>The address proven a moment ago in this browser, waiting for a name; null when there is none or it is too old.</summary>
    public PendingSignUp? ReadPendingSignUp(HttpContext context) => Read<PendingSignUp>(context, SignUpStep);

    public void ForgetPendingSignUp(HttpContext context) => Forget(context, SignUpStep);

    /// <summary>The account whose password may be reset in this browser now; null when there is none or it is too old.</summary>
    public PendingReset? ReadPendingReset(HttpContext context) => Read<PendingReset>(context, ResetStep);

    public void ForgetPendingReset(HttpContext context) => Forget(context, ResetStep);

    private async Task<bool> CanUseAsync(ApplicationUser user)
    {
        if (!user.IsActive)
        {
            SecurityLog.SignInFailed(logger, user.Id);
            return false;
        }

        // The code reached the mailbox, which proves the address belongs to this person.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
        }

        return true;
    }

    private void Remember<T>(HttpContext context, PendingStep step, T value)
    {
        // Signed, encrypted and time-limited by data protection: the browser can carry it but cannot read or change it.
        var protectedValue = Protector(step).Protect(JsonSerializer.Serialize(value), PendingLifetime);
        context.Response.Cookies.Append(CookieName(step), protectedValue, CookieOptions());
    }

    private T? Read<T>(HttpContext context, PendingStep step)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Cookies.TryGetValue(CookieName(step), out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(Protector(step).Unprotect(value));
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException)
        {
            return null;
        }
    }

    private void Forget(HttpContext context, PendingStep step)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.Cookies.Delete(CookieName(step), CookieOptions());
    }

    private string CookieName(PendingStep step) => cookies.Value.Prefix + step.Cookie;

    private ITimeLimitedDataProtector Protector(PendingStep step) => dataProtection.CreateProtector(step.Purpose).ToTimeLimitedDataProtector();

    private CookieOptions CookieOptions() => new()
    {
        HttpOnly = true,
        Secure = cookies.Value.SecurePolicy == CookieSecurePolicy.Always,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = PendingLifetime,
        IsEssential = true,
    };

    private sealed record PendingStep(string Cookie, string Purpose);
}

public sealed record PendingSignUp(string Email, string? ReturnUrl);

public sealed record PendingReset(string UserId, string Token);

using System.ComponentModel.DataAnnotations;
using Helpdesk.Web.Data;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using QRCoder;

namespace Helpdesk.Web.Pages.Account;

/// <summary>Turns the second sign-in step (a code from an authenticator app) on and off and manages recovery codes.</summary>
public class TwoFactorModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IOptions<DemoOptions> demo,
    ILogger<TwoFactorModel> logger) : PageModel
{
    private const int RecoveryCodeCount = 10;

    [TempData]
    public string? Notice { get; set; }

    public bool Enabled { get; private set; }

    /// <summary>The shared demo accounts have no password of their own, so they cannot use a second step.</summary>
    public bool IsReadOnly { get; private set; }

    public string SharedKey { get; private set; } = string.Empty;

    public string QrCode { get; private set; } = string.Empty;

    public int RecoveryCodesLeft { get; private set; }

    /// <summary>Shown once, right after they were made. Only their hashes are kept.</summary>
    public IReadOnlyList<string>? NewRecoveryCodes { get; private set; }

    public VerifyInput Verify { get; private set; } = new();

    public PasswordInput Confirm { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await LoadAsync();
        return user is null ? Challenge() : await ShowAsync(user);
    }

    public async Task<IActionResult> OnPostEnableAsync([Bind(Prefix = nameof(Verify))] VerifyInput input)
    {
        var user = await LoadAsync();

        if (user is null || IsReadOnly)
        {
            return Forbid();
        }

        Verify = input;

        if (ModelState.IsValid)
        {
            var valid = await userManager.VerifyTwoFactorTokenAsync(
                user, userManager.Options.Tokens.AuthenticatorTokenProvider, NormaliseCode(input.Code));

            if (valid)
            {
                await userManager.SetTwoFactorEnabledAsync(user, true);
                var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);
                SecurityLog.TwoFactorChanged(logger, user.Id, "on");

                // Turning it on changes the security stamp; refresh the cookie so this session stays signed in.
                await signInManager.RefreshSignInAsync(user);
                Enabled = true;
                return await ShowAsync(user, codes?.ToList());
            }

            ModelState.AddModelError("Verify.Code", "The code is not correct. Check the code in the app and try again.");
        }

        return await ShowAsync(user);
    }

    public async Task<IActionResult> OnPostNewCodesAsync([Bind(Prefix = nameof(Confirm))] PasswordInput input)
    {
        var user = await LoadAsync();

        if (user is null || IsReadOnly || !Enabled)
        {
            return Forbid();
        }

        Confirm = input;

        if (ModelState.IsValid && await PasswordIsCorrectAsync(user, input.Password))
        {
            var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);
            return await ShowAsync(user, codes?.ToList());
        }

        return await ShowAsync(user);
    }

    public async Task<IActionResult> OnPostDisableAsync([Bind(Prefix = nameof(Confirm))] PasswordInput input)
    {
        var user = await LoadAsync();

        if (user is null || IsReadOnly || !Enabled)
        {
            return Forbid();
        }

        Confirm = input;

        if (ModelState.IsValid && await PasswordIsCorrectAsync(user, input.Password))
        {
            await userManager.SetTwoFactorEnabledAsync(user, false);
            await userManager.ResetAuthenticatorKeyAsync(user);
            SecurityLog.TwoFactorChanged(logger, user.Id, "off");
            await signInManager.RefreshSignInAsync(user);
            Notice = "Two-factor authentication is off.";
            return RedirectToPage();
        }

        return await ShowAsync(user);
    }

    /// <summary>Checks the password; a wrong one counts towards the account lock-out like a failed sign-in does.</summary>
    private async Task<bool> PasswordIsCorrectAsync(ApplicationUser user, string password)
    {
        if (await userManager.CheckPasswordAsync(user, password))
        {
            return true;
        }

        await userManager.AccessFailedAsync(user);
        ModelState.AddModelError("Confirm.Password", "The password is not correct.");
        return false;
    }

    private async Task<ApplicationUser?> LoadAsync()
    {
        var user = await userManager.GetUserAsync(User);

        if (user is not null)
        {
            Enabled = await userManager.GetTwoFactorEnabledAsync(user);
            IsReadOnly = demo.Value.Enabled && DatabaseSeeder.IsDemoAddress(user.Email);
        }

        return user;
    }

    private async Task<IActionResult> ShowAsync(ApplicationUser user, IReadOnlyList<string>? newCodes = null)
    {
        NewRecoveryCodes = newCodes;

        if (IsReadOnly)
        {
            return Page();
        }

        if (Enabled)
        {
            RecoveryCodesLeft = await userManager.CountRecoveryCodesAsync(user);
            return Page();
        }

        var key = await userManager.GetAuthenticatorKeyAsync(user);

        if (string.IsNullOrEmpty(key))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);

            // A new key renews the security stamp, which would sign this very session out on its next request.
            await signInManager.RefreshSignInAsync(user);
        }

        SharedKey = string.Join(' ', Chunk(key!, 4));
        QrCode = QrCodeDataUri(
            $"otpauth://totp/Helpdesk:{Uri.EscapeDataString(user.Email ?? user.UserName ?? "user")}"
            + $"?secret={key!.ToUpperInvariant()}&issuer=Helpdesk&digits=6");

        return Page();
    }

    private static IEnumerable<string> Chunk(string value, int size)
    {
        for (var i = 0; i < value.Length; i += size)
        {
            yield return value.Substring(i, Math.Min(size, value.Length - i));
        }
    }

    private static string NormaliseCode(string code) => code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);

    private static string QrCodeDataUri(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        return "data:image/png;base64," + Convert.ToBase64String(new PngByteQRCode(data).GetGraphic(6));
    }

    public class VerifyInput
    {
        [Required]
        [StringLength(10, MinimumLength = 6)]
        [Display(Name = "Code from the app")]
        public string Code { get; set; } = string.Empty;
    }

    public class PasswordInput
    {
        [Required]
        [DataType(DataType.Password)]
        [StringLength(256)]
        [Display(Name = "Your password")]
        public string Password { get; set; } = string.Empty;
    }
}

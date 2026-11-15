using System.Text.Encodings.Web;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Email;

namespace Helpdesk.Web.Features.SignIn;

/// <summary>The messages that carry a sign-in or password reset code and link.</summary>
public static class SignInEmails
{
    public static EmailMessage For(string to, ChallengePurpose purpose, IssuedChallenge challenge, string linkUrl, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        var (subject, intro, button) = purpose == ChallengePurpose.PasswordReset
            ? ($"{challenge.Code} is your Helpdesk password reset code", "Use this code to choose a new password for Helpdesk:", "Choose a new password")
            : ($"{challenge.Code} is your Helpdesk sign-in code", "Use this code to sign in to Helpdesk:", "Sign in to Helpdesk");

        var minutes = (int)lifetime.TotalMinutes;
        var ignore = "If you did not request it, ignore this e-mail.";

        var text = $"""
            {intro}

            {challenge.Code}

            Or open this link: {linkUrl}

            The code is valid for {minutes} minutes.
            {ignore}
            """;

        var html = HtmlEncoder.Default;
        var body = $"""
            <!DOCTYPE html>
            <html lang="en">
            <body style="margin:0;padding:24px;background:#f3f5f5;font-family:Segoe UI,Arial,sans-serif;color:#1d2b2a;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
                <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="max-width:480px;background:#ffffff;border-radius:12px;padding:32px;">
                  <tr><td style="font-size:18px;font-weight:700;padding-bottom:16px;">Helpdesk</td></tr>
                  <tr><td style="font-size:15px;padding-bottom:12px;">{html.Encode(intro)}</td></tr>
                  <tr><td style="font-size:32px;font-weight:700;letter-spacing:6px;padding:8px 0 20px;">{html.Encode(challenge.Code)}</td></tr>
                  <tr><td style="padding-bottom:20px;">
                    <a href="{html.Encode(linkUrl)}" style="display:inline-block;background:#0f766e;color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:8px;font-weight:600;">{html.Encode(button)}</a>
                  </td></tr>
                  <tr><td style="font-size:13px;color:#5b6b6a;">The code is valid for {minutes} minutes. {html.Encode(ignore)}</td></tr>
                </table>
              </td></tr></table>
            </body>
            </html>
            """;

        return new EmailMessage(to, subject, text, body);
    }
}

using System.Text.Encodings.Web;
using Helpdesk.Web.Features.Email;

namespace Helpdesk.Web.Features.Workspaces;

/// <summary>The message that brings someone into a help desk.</summary>
public static class InvitationEmails
{
    public static EmailMessage For(string to, string inviterName, string workspaceName, string role, string linkUrl, TimeSpan lifetime)
    {
        var subject = $"{inviterName} invited you to {workspaceName} on Helpdesk";
        var intro = $"{inviterName} invited you to join {workspaceName} on Helpdesk as {Article(role)} {role.ToLowerInvariant()}.";
        var days = (int)lifetime.TotalDays;
        var ignore = "If you do not know this help desk, ignore this e-mail.";

        var text = $"""
            {intro}

            Accept the invitation: {linkUrl}

            The invitation is valid for {days} days.
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
                  <tr><td style="font-size:15px;padding-bottom:20px;">{html.Encode(intro)}</td></tr>
                  <tr><td style="padding-bottom:20px;">
                    <a href="{html.Encode(linkUrl)}" style="display:inline-block;background:#0f766e;color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:8px;font-weight:600;">Accept the invitation</a>
                  </td></tr>
                  <tr><td style="font-size:13px;color:#5b6b6a;">The invitation is valid for {days} days. {html.Encode(ignore)}</td></tr>
                </table>
              </td></tr></table>
            </body>
            </html>
            """;

        return new EmailMessage(to, subject, text, body);
    }

    private static string Article(string role) => role.Length > 0 && "AEIOU".Contains(char.ToUpperInvariant(role[0])) ? "an" : "a";
}

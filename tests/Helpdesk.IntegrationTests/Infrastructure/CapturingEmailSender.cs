using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Helpdesk.Web.Features.Email;

namespace Helpdesk.IntegrationTests.Infrastructure;

/// <summary>Keeps every message the application sends, so tests can read codes and links like a person would.</summary>
public sealed partial class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyList<EmailMessage> Messages => [.. _messages];

    /// <summary>While set, every message fails like a mail server that refuses it.</summary>
    public bool Refusing { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (Refusing)
        {
            throw new InvalidOperationException("The mail server refused the message.");
        }

        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> To(string address)
        => Messages.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase)).ToList();

    public EmailMessage LastTo(string address)
        => To(address) is { Count: > 0 } sent ? sent[^1] : throw new InvalidOperationException($"No e-mail was sent to {address}.");

    public static string CodeIn(EmailMessage message) => CodePattern().Match(message.Text).Groups[1].Value;

    public static string LinkIn(EmailMessage message) => LinkPattern().Match(message.Text).Value;

    /// <summary>The invitation link of a message, as a path the test client can open.</summary>
    public static string InvitationLinkIn(EmailMessage message) => new Uri(InvitationPattern().Match(message.Text).Value).PathAndQuery;

    [GeneratedRegex(@"^\s*(\d{6})\s*$", RegexOptions.Multiline)]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"https?://\S+/Account/Link\?token=\S+")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"https?://\S+/Account/Invitation\?token=\S+")]
    private static partial Regex InvitationPattern();
}

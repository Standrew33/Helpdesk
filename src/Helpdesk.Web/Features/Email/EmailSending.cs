using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Helpdesk.Web.Features.Email;

/// <summary>A message with a plain-text and an HTML version; mail programs show the one they support.</summary>
public sealed record EmailMessage(string To, string Subject, string Text, string Html);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>SMTP server of the mail provider. Without it, messages are written to <see cref="PickupFolder"/>.</summary>
    public string? Host { get; set; }

    /// <summary>587 (STARTTLS) or 465 (TLS from the start).</summary>
    public int Port { get; set; } = 587;

    public string? User { get; set; }

    public string? Password { get; set; }

    /// <summary>Sender address. It must belong to a domain the provider has verified, or mail ends up as spam.</summary>
    public string From { get; set; } = "helpdesk@localhost";

    public string FromName { get; set; } = "Helpdesk";

    /// <summary>Where development messages go when no SMTP server is configured. Defaults to App_Data/mail.</summary>
    public string? PickupFolder { get; set; }
}

public static class EmailRegistration
{
    /// <summary>
    /// SMTP when a server is configured. Without one, messages are saved as .eml files for development and tests;
    /// in production that would silently lose every sign-in code, so the application refuses to start instead.
    /// </summary>
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(
                options => !environment.IsProduction() || !string.IsNullOrWhiteSpace(options.Host),
                "E-mail is not configured: set Email:Host, Email:User, Email:Password and Email:From. Sign-in codes cannot be delivered without it.")
            .ValidateOnStart();

        services.AddSingleton<IEmailSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<EmailOptions>>();

            if (!string.IsNullOrWhiteSpace(options.Value.Host))
            {
                return new SmtpEmailSender(options);
            }

            if (environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "E-mail is not configured: set Email:Host, Email:User, Email:Password and Email:From. "
                    + "Sign-in codes cannot be delivered without it.");
            }

            var folder = options.Value.PickupFolder ?? Path.Combine(environment.ContentRootPath, "App_Data", "mail");
            return new PickupFolderEmailSender(folder, options, sp.GetRequiredService<ILogger<PickupFolderEmailSender>>());
        });

        return services;
    }

    internal static MimeMessage ToMime(EmailMessage message, EmailOptions options)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.Text, HtmlBody = message.Html }.ToMessageBody();
        return mime;
    }
}

/// <summary>Sends through the SMTP server of a mail provider, always over an encrypted connection.</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;

        using var client = new SmtpClient();
        var security = settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(settings.Host!, settings.Port, security, ct);

        if (!string.IsNullOrEmpty(settings.User))
        {
            await client.AuthenticateAsync(settings.User, settings.Password ?? string.Empty, ct);
        }

        await client.SendAsync(new SendRequest(EmailRegistration.ToMime(message, settings)), ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}

/// <summary>Development stand-in: every message becomes an .eml file that any mail program opens.</summary>
public sealed partial class PickupFolderEmailSender(string folder, IOptions<EmailOptions> options, ILogger<PickupFolderEmailSender> logger) : IEmailSender
{
    public string Folder => folder;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.eml");
        await using (var file = File.Create(path))
        {
            await EmailRegistration.ToMime(message, options.Value).WriteToAsync(file, ct);
        }

        Saved(logger, message.Subject, path);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "E-mail \"{Subject}\" saved to {Path}")]
    private static partial void Saved(ILogger logger, string subject, string path);
}

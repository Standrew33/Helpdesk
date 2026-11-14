namespace Helpdesk.Web.Security;

/// <summary>
/// Security-relevant events. They name accounts by id and never contain passwords, codes or e-mail addresses,
/// so the log can be read and kept without becoming a store of personal data.
/// </summary>
public static partial class SecurityLog
{
    [LoggerMessage(EventId = 1013, Level = LogLevel.Warning, Message = "Too many requests to {Path} from {Client}")]
    public static partial void RequestLimited(ILogger logger, PathString path, string client);
}

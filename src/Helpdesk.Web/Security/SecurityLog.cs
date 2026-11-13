namespace Helpdesk.Web.Security;

/// <summary>
/// Security-relevant events. They name accounts by id and never contain passwords, codes or e-mail addresses,
/// so the log can be read and kept without becoming a store of personal data.
/// </summary>
public static partial class SecurityLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "User {UserId} signed in")]
    public static partial void SignedIn(ILogger logger, string userId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Failed sign-in for user {UserId}")]
    public static partial void SignInFailed(ILogger logger, string userId);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Failed sign-in for an unknown or deactivated account")]
    public static partial void SignInFailedUnknown(ILogger logger);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "User {UserId} is locked out after repeated failures")]
    public static partial void LockedOut(ILogger logger, string userId);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "Account {UserId} was registered")]
    public static partial void Registered(ILogger logger, string userId);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Warning, Message = "A registration was dropped by the spam trap")]
    public static partial void RegistrationTrapped(ILogger logger);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "User {UserId} changed the password")]
    public static partial void PasswordChanged(ILogger logger, string userId);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Information, Message = "User {UserId} turned two-factor authentication {State}")]
    public static partial void TwoFactorChanged(ILogger logger, string userId, string state);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Information, Message = "User {UserId} signed in with a recovery code")]
    public static partial void RecoveryCodeUsed(ILogger logger, string userId);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Information, Message = "Administrator {ActorId} changed the role of {UserId} to {Role}")]
    public static partial void RoleChanged(ILogger logger, string actorId, string userId, string role);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Information, Message = "Administrator {ActorId} set the account {UserId} active: {Active}")]
    public static partial void ActiveChanged(ILogger logger, string actorId, string userId, bool active);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Warning, Message = "User {UserId} asked for ticket {TicketId} without being allowed to see it")]
    public static partial void TicketAccessRefused(ILogger logger, string userId, int ticketId);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Warning, Message = "Too many requests to {Path} from {Client}")]
    public static partial void RequestLimited(ILogger logger, PathString path, string client);

    [LoggerMessage(EventId = 1014, Level = LogLevel.Warning, Message = "A file for ticket {TicketId} was refused: its content does not match its type")]
    public static partial void UploadRefused(ILogger logger, int ticketId);
}

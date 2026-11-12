namespace Helpdesk.Web.Data;

internal static partial class StartupLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Database is not ready (attempt {Attempt} of {Max}), retrying")]
    public static partial void DatabaseNotReady(ILogger logger, Exception exception, int attempt, int max);
}

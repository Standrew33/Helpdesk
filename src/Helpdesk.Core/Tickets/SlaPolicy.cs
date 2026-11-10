namespace Helpdesk.Core.Tickets;

/// <summary>Resolution targets per priority, counted in calendar hours.</summary>
public static class SlaPolicy
{
    public static TimeSpan ResolutionTime(TicketPriority priority) => priority switch
    {
        TicketPriority.Urgent => TimeSpan.FromHours(4),
        TicketPriority.High => TimeSpan.FromHours(8),
        TicketPriority.Normal => TimeSpan.FromHours(24),
        TicketPriority.Low => TimeSpan.FromHours(72),
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown priority."),
    };

    public static DateTimeOffset CalculateDueAt(TicketPriority priority, DateTimeOffset createdAt)
        => createdAt + ResolutionTime(priority);
}

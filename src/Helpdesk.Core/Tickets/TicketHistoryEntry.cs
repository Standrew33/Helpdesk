namespace Helpdesk.Core.Tickets;

/// <summary>One recorded change of a ticket: who changed which field, from what to what.</summary>
public sealed class TicketHistoryEntry
{
    internal TicketHistoryEntry(string actorId, string field, string? oldValue, string? newValue, DateTimeOffset changedAt)
    {
        ActorId = actorId;
        Field = field;
        OldValue = oldValue;
        NewValue = newValue;
        ChangedAt = changedAt;
    }

    // Required by EF Core.
    private TicketHistoryEntry()
    {
        ActorId = string.Empty;
        Field = string.Empty;
    }

    public int Id { get; private set; }

    public int TicketId { get; private set; }

    public string ActorId { get; private set; }

    public string Field { get; private set; }

    public string? OldValue { get; private set; }

    public string? NewValue { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }
}

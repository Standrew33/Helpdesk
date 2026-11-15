namespace Helpdesk.Core.Tickets;

/// <summary>Ordered from the least to the most pressing; the order is used for sorting.</summary>
public enum TicketPriority
{
    Backlog = 0,
    Low = 1,
    Normal = 2,
    High = 3,
    Urgent = 4,
}

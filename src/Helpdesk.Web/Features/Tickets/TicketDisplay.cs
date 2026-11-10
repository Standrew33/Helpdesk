using Helpdesk.Core.Tickets;

namespace Helpdesk.Web.Features.Tickets;

public static class TicketDisplay
{
    public static string StatusText(TicketStatus status) => status switch
    {
        TicketStatus.New => "New",
        TicketStatus.InProgress => "In progress",
        TicketStatus.WaitingForCustomer => "Waiting for customer",
        TicketStatus.Closed => "Closed",
        _ => status.ToString(),
    };

    public static string StatusBadge(TicketStatus status) => status switch
    {
        TicketStatus.New => "text-bg-primary",
        TicketStatus.InProgress => "text-bg-info",
        TicketStatus.WaitingForCustomer => "text-bg-warning",
        TicketStatus.Closed => "text-bg-secondary",
        _ => "text-bg-light",
    };

    public static string PriorityBadge(TicketPriority priority) => priority switch
    {
        TicketPriority.Urgent => "text-bg-danger",
        TicketPriority.High => "text-bg-warning",
        TicketPriority.Normal => "text-bg-light border",
        TicketPriority.Low => "text-bg-light border",
        _ => "text-bg-light",
    };

    public static string Date(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
}

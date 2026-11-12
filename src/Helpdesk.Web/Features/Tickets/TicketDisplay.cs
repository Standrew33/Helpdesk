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

    public static string StatusClass(TicketStatus status) => status switch
    {
        TicketStatus.New => "pill pill-new",
        TicketStatus.InProgress => "pill pill-progress",
        TicketStatus.WaitingForCustomer => "pill pill-waiting",
        TicketStatus.Closed => "pill pill-closed",
        _ => "pill",
    };

    public static string DotClass(TicketStatus status) => status switch
    {
        TicketStatus.New => "dot-new",
        TicketStatus.InProgress => "dot-progress",
        TicketStatus.WaitingForCustomer => "dot-waiting",
        TicketStatus.Closed => "dot-closed",
        _ => "",
    };

    public static string PriorityClass(TicketPriority priority) => priority switch
    {
        TicketPriority.Urgent => "tag tag-urgent",
        TicketPriority.High => "tag tag-high",
        TicketPriority.Normal => "tag tag-normal",
        TicketPriority.Low => "tag tag-low",
        _ => "tag",
    };

    public static string Date(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
}

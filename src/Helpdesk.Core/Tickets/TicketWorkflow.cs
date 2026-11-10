using Helpdesk.Core.Users;

namespace Helpdesk.Core.Tickets;

/// <summary>Defines which status transitions are allowed and who may perform them.</summary>
public static class TicketWorkflow
{
    public static bool IsAllowed(TicketStatus from, TicketStatus to, UserRole role) => (from, to) switch
    {
        (TicketStatus.New, TicketStatus.InProgress) => role.IsStaff(),
        (TicketStatus.InProgress, TicketStatus.WaitingForCustomer) => role.IsStaff(),
        (TicketStatus.WaitingForCustomer, TicketStatus.InProgress) => true,
        (TicketStatus.New or TicketStatus.InProgress or TicketStatus.WaitingForCustomer, TicketStatus.Closed) => true,
        (TicketStatus.Closed, TicketStatus.InProgress) => role.IsStaff(),
        _ => false,
    };
}

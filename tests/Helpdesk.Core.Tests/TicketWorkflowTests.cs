using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;

namespace Helpdesk.Core.Tests;

public class TicketWorkflowTests
{
    [Theory]
    [InlineData(TicketStatus.New, TicketStatus.InProgress, UserRole.Operator, true)]
    [InlineData(TicketStatus.New, TicketStatus.InProgress, UserRole.Admin, true)]
    [InlineData(TicketStatus.New, TicketStatus.InProgress, UserRole.Customer, false)]
    [InlineData(TicketStatus.InProgress, TicketStatus.WaitingForCustomer, UserRole.Operator, true)]
    [InlineData(TicketStatus.InProgress, TicketStatus.WaitingForCustomer, UserRole.Customer, false)]
    [InlineData(TicketStatus.WaitingForCustomer, TicketStatus.InProgress, UserRole.Customer, true)]
    [InlineData(TicketStatus.New, TicketStatus.Closed, UserRole.Customer, true)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Closed, UserRole.Operator, true)]
    [InlineData(TicketStatus.Closed, TicketStatus.InProgress, UserRole.Operator, true)]
    [InlineData(TicketStatus.Closed, TicketStatus.InProgress, UserRole.Customer, false)]
    [InlineData(TicketStatus.New, TicketStatus.WaitingForCustomer, UserRole.Operator, false)]
    [InlineData(TicketStatus.Closed, TicketStatus.New, UserRole.Admin, false)]
    [InlineData(TicketStatus.Closed, TicketStatus.WaitingForCustomer, UserRole.Admin, false)]
    public void IsAllowed_follows_the_workflow(TicketStatus from, TicketStatus to, UserRole role, bool expected)
    {
        Assert.Equal(expected, TicketWorkflow.IsAllowed(from, to, role));
    }
}

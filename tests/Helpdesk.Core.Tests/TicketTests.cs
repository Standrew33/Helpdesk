using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;

namespace Helpdesk.Core.Tests;

public class TicketTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    private static readonly Actor Customer = new("customer-1", UserRole.Customer);
    private static readonly Actor OtherCustomer = new("customer-2", UserRole.Customer);
    private static readonly Actor Operator = new("operator-1", UserRole.Operator);
    private static readonly Actor Admin = new("admin-1", UserRole.Admin);

    private static Ticket NewTicket(TicketPriority priority = TicketPriority.Normal)
    {
        // Customers cannot open urgent tickets; staff raises the priority afterwards.
        var ticket = Ticket.Create("Printer is down", "It prints blank pages.", priority == TicketPriority.Urgent ? TicketPriority.High : priority, Customer, Now);

        if (priority == TicketPriority.Urgent)
        {
            ticket.ChangePriority(Operator, TicketPriority.Urgent, Now);
        }

        return ticket;
    }

    [Fact]
    public void Create_sets_initial_state_and_deadline()
    {
        var ticket = NewTicket(TicketPriority.High);

        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Equal(Customer.Id, ticket.CustomerId);
        Assert.Null(ticket.AssigneeId);
        Assert.Equal(Now.AddHours(8), ticket.DueAt);
        Assert.Single(ticket.History);
        Assert.Equal("Created", ticket.History[0].Field);
    }

    [Fact]
    public void Create_trims_text()
    {
        var ticket = Ticket.Create("  Subject  ", "  Body  ", TicketPriority.Low, Customer, Now);

        Assert.Equal("Subject", ticket.Subject);
        Assert.Equal("Body", ticket.Description);
    }

    [Theory]
    [InlineData("", "text")]
    [InlineData("   ", "text")]
    [InlineData("subject", "")]
    public void Create_requires_subject_and_description(string subject, string description)
    {
        Assert.Throws<DomainException>(() => Ticket.Create(subject, description, TicketPriority.Normal, Customer, Now));
    }

    [Fact]
    public void Create_rejects_too_long_subject()
    {
        var subject = new string('a', Ticket.MaxSubjectLength + 1);

        Assert.Throws<DomainException>(() => Ticket.Create(subject, "text", TicketPriority.Normal, Customer, Now));
    }

    [Fact]
    public void Create_is_allowed_for_customers_only()
    {
        Assert.Throws<DomainException>(() => Ticket.Create("s", "d", TicketPriority.Normal, Operator, Now));
    }

    [Fact]
    public void Create_does_not_allow_customers_to_set_urgent_priority()
    {
        Assert.Throws<DomainException>(() => Ticket.Create("s", "d", TicketPriority.Urgent, Customer, Now));
    }

    [Fact]
    public void Customer_sees_only_own_tickets_but_staff_sees_all()
    {
        var ticket = NewTicket();

        Assert.True(ticket.CanBeViewedBy(Customer));
        Assert.False(ticket.CanBeViewedBy(OtherCustomer));
        Assert.True(ticket.CanBeViewedBy(Operator));
        Assert.True(ticket.CanBeViewedBy(Admin));
    }

    [Fact]
    public void Foreign_customer_cannot_change_the_ticket()
    {
        var ticket = NewTicket();

        Assert.Throws<DomainException>(() => ticket.AddComment(OtherCustomer, "hello", false, Now));
        Assert.Throws<DomainException>(() => ticket.ChangeStatus(OtherCustomer, TicketStatus.Closed, Now));
    }

    [Fact]
    public void ChangeStatus_records_history_and_updates_timestamps()
    {
        var ticket = NewTicket();
        var later = Now.AddHours(1);

        ticket.ChangeStatus(Operator, TicketStatus.InProgress, later);

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal(later, ticket.UpdatedAt);
        var entry = ticket.History[^1];
        Assert.Equal(("Status", "New", "InProgress", Operator.Id), (entry.Field, entry.OldValue, entry.NewValue, entry.ActorId));
    }

    [Fact]
    public void ChangeStatus_rejects_transitions_outside_the_workflow()
    {
        var ticket = NewTicket();

        Assert.Throws<DomainException>(() => ticket.ChangeStatus(Customer, TicketStatus.InProgress, Now));
        Assert.Throws<DomainException>(() => ticket.ChangeStatus(Operator, TicketStatus.WaitingForCustomer, Now));
    }

    [Fact]
    public void ChangeStatus_rejects_the_same_status()
    {
        var ticket = NewTicket();

        Assert.Throws<DomainException>(() => ticket.ChangeStatus(Operator, TicketStatus.New, Now));
    }

    [Fact]
    public void Closing_sets_ClosedAt_and_reopening_clears_it()
    {
        var ticket = NewTicket();
        var closedAt = Now.AddHours(2);

        ticket.ChangeStatus(Customer, TicketStatus.Closed, closedAt);
        Assert.Equal(closedAt, ticket.ClosedAt);

        ticket.ChangeStatus(Operator, TicketStatus.InProgress, closedAt.AddHours(1));
        Assert.Null(ticket.ClosedAt);
    }

    [Fact]
    public void Customer_cannot_reopen_a_closed_ticket()
    {
        var ticket = NewTicket();
        ticket.ChangeStatus(Customer, TicketStatus.Closed, Now);

        Assert.Throws<DomainException>(() => ticket.ChangeStatus(Customer, TicketStatus.InProgress, Now));
    }

    [Fact]
    public void Assign_is_for_staff_and_recorded()
    {
        var ticket = NewTicket();

        Assert.Throws<DomainException>(() => ticket.Assign(Customer, Operator.Id, Now));

        ticket.Assign(Admin, Operator.Id, Now);

        Assert.Equal(Operator.Id, ticket.AssigneeId);
        Assert.Equal("Assignee", ticket.History[^1].Field);
    }

    [Fact]
    public void Assign_to_the_same_user_is_rejected()
    {
        var ticket = NewTicket();
        ticket.Assign(Operator, Operator.Id, Now);

        Assert.Throws<DomainException>(() => ticket.Assign(Admin, Operator.Id, Now));
    }

    [Fact]
    public void Assign_can_unassign()
    {
        var ticket = NewTicket();
        ticket.Assign(Operator, Operator.Id, Now);

        ticket.Assign(Operator, null, Now);

        Assert.Null(ticket.AssigneeId);
    }

    [Fact]
    public void ChangePriority_recalculates_the_deadline_from_creation_time()
    {
        var ticket = NewTicket(TicketPriority.Low);

        ticket.ChangePriority(Operator, TicketPriority.Urgent, Now.AddHours(1));

        Assert.Equal(Now.AddHours(4), ticket.DueAt);
        Assert.Equal("Priority", ticket.History[^1].Field);
    }

    [Fact]
    public void ChangePriority_is_for_staff_only()
    {
        var ticket = NewTicket();

        Assert.Throws<DomainException>(() => ticket.ChangePriority(Customer, TicketPriority.Urgent, Now));
    }

    [Fact]
    public void Closed_ticket_cannot_be_assigned_commented_or_reprioritized()
    {
        var ticket = NewTicket();
        ticket.ChangeStatus(Customer, TicketStatus.Closed, Now);

        Assert.Throws<DomainException>(() => ticket.Assign(Operator, Operator.Id, Now));
        Assert.Throws<DomainException>(() => ticket.AddComment(Customer, "hi", false, Now));
        Assert.Throws<DomainException>(() => ticket.ChangePriority(Operator, TicketPriority.High, Now));
    }

    [Fact]
    public void Customer_cannot_add_internal_notes()
    {
        var ticket = NewTicket();

        Assert.Throws<DomainException>(() => ticket.AddComment(Customer, "secret", true, Now));
    }

    [Fact]
    public void Customers_do_not_see_internal_notes()
    {
        var ticket = NewTicket();
        ticket.AddComment(Customer, "public question", false, Now);
        ticket.AddComment(Operator, "internal remark", true, Now);

        Assert.Single(ticket.CommentsVisibleTo(Customer));
        Assert.Equal(2, ticket.CommentsVisibleTo(Operator).Count());
    }

    [Fact]
    public void Customer_reply_moves_a_waiting_ticket_back_to_in_progress()
    {
        var ticket = NewTicket();
        ticket.ChangeStatus(Operator, TicketStatus.InProgress, Now);
        ticket.ChangeStatus(Operator, TicketStatus.WaitingForCustomer, Now);

        ticket.AddComment(Customer, "Here are the details", false, Now.AddHours(1));

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal("Status", ticket.History[^1].Field);
    }

    [Fact]
    public void Operator_comment_does_not_change_the_status()
    {
        var ticket = NewTicket();
        ticket.ChangeStatus(Operator, TicketStatus.InProgress, Now);
        ticket.ChangeStatus(Operator, TicketStatus.WaitingForCustomer, Now);

        ticket.AddComment(Operator, "Please send a photo", false, Now);

        Assert.Equal(TicketStatus.WaitingForCustomer, ticket.Status);
    }

    [Fact]
    public void AddComment_requires_text()
    {
        var ticket = NewTicket();

        Assert.Throws<DomainException>(() => ticket.AddComment(Customer, "  ", false, Now));
    }

    [Fact]
    public void IsSlaBreached_checks_open_tickets_against_now()
    {
        var ticket = NewTicket(TicketPriority.Urgent);

        Assert.False(ticket.IsSlaBreached(Now.AddHours(3)));
        Assert.True(ticket.IsSlaBreached(Now.AddHours(5)));
    }

    [Fact]
    public void IsSlaBreached_uses_closing_time_for_closed_tickets()
    {
        var ticket = NewTicket(TicketPriority.Urgent);
        ticket.ChangeStatus(Customer, TicketStatus.Closed, Now.AddHours(3));

        Assert.False(ticket.IsSlaBreached(Now.AddDays(30)));
    }
}

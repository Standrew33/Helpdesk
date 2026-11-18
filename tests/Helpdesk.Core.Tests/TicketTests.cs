using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;

namespace Helpdesk.Core.Tests;

public class TicketTests
{
    private const int Workspace = 1;
    private const int OtherWorkspace = 2;

    private static readonly DateTimeOffset Now = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    private static readonly Actor Customer = new("customer-1", UserRole.Customer, Workspace);
    private static readonly Actor OtherCustomer = new("customer-2", UserRole.Customer, Workspace);
    private static readonly Actor Operator = new("operator-1", UserRole.Operator, Workspace);
    private static readonly Actor Admin = new("admin-1", UserRole.Admin, Workspace);

    private static Ticket NewTicket(TicketPriority priority = TicketPriority.Normal)
    {
        // Customers cannot open urgent tickets; staff raises the priority afterwards.
        var ticket = Ticket.Create(1, "Printer is down", "It prints blank pages.", priority == TicketPriority.Urgent ? TicketPriority.High : priority, Customer, Now);

        if (priority == TicketPriority.Urgent)
        {
            ticket.Edit(Operator, ticket.Subject, ticket.Description, TicketPriority.Urgent, Now);
        }

        return ticket;
    }

    [Fact]
    public void Create_sets_initial_state_without_a_due_date()
    {
        var ticket = NewTicket(TicketPriority.High);

        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Equal(Customer.Id, ticket.CustomerId);
        Assert.Null(ticket.AssigneeId);
        Assert.Null(ticket.DueDate);
        Assert.Single(ticket.History);
        Assert.Equal("Created", ticket.History[0].Field);
    }

    [Fact]
    public void Create_puts_the_ticket_in_the_workspace_of_the_customer()
    {
        var ticket = Ticket.Create(42, "Printer is down", "It prints blank pages.", TicketPriority.Normal, Customer with { WorkspaceId = 7 }, Now);

        Assert.Equal(7, ticket.WorkspaceId);
        Assert.Equal(42, ticket.Number);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Create_needs_a_workspace_and_a_number(int workspaceId, int number)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => Ticket.Create(number, "Printer is down", "It prints blank pages.", TicketPriority.Normal, Customer with { WorkspaceId = workspaceId }, Now));

    [Fact]
    public void Create_trims_text()
    {
        var ticket = Ticket.Create(1, "  Subject  ", "  Body  ", TicketPriority.Low, Customer, Now);

        Assert.Equal("Subject", ticket.Subject);
        Assert.Equal("Body", ticket.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_a_subject(string subject)
    {
        Assert.Throws<DomainException>(() => Ticket.Create(1, subject, "text", TicketPriority.Normal, Customer, Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void The_description_may_be_left_empty(string? description)
    {
        var ticket = Ticket.Create(1, "Subject", description!, TicketPriority.Normal, Customer, Now);

        Assert.Equal(string.Empty, ticket.Description);
    }

    [Fact]
    public void Create_rejects_too_long_subject()
    {
        var subject = new string('a', Ticket.MaxSubjectLength + 1);

        Assert.Throws<DomainException>(() => Ticket.Create(1, subject, "text", TicketPriority.Normal, Customer, Now));
    }

    [Fact]
    public void Create_is_allowed_for_customers_only()
    {
        Assert.Throws<DomainException>(() => Ticket.Create(1, "s", "d", TicketPriority.Normal, Operator, Now));
    }

    [Fact]
    public void Create_does_not_allow_customers_to_set_urgent_priority()
    {
        Assert.Throws<DomainException>(() => Ticket.Create(1, "s", "d", TicketPriority.Urgent, Customer, Now));
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
    public void Nobody_sees_a_ticket_of_another_workspace()
    {
        var ticket = NewTicket();

        Assert.False(ticket.CanBeViewedBy(Operator with { WorkspaceId = OtherWorkspace }));
        Assert.False(ticket.CanBeViewedBy(Admin with { WorkspaceId = OtherWorkspace }));

        // Even an account with the same id, should one ever exist, is someone else in another workspace.
        Assert.False(ticket.CanBeViewedBy(Customer with { WorkspaceId = OtherWorkspace }));
    }

    [Fact]
    public void Staff_of_another_workspace_cannot_change_the_ticket()
    {
        var ticket = NewTicket();
        var stranger = Admin with { WorkspaceId = OtherWorkspace };

        Assert.Throws<DomainException>(() => ticket.ChangeStatus(stranger, TicketStatus.InProgress, Now));
        Assert.Throws<DomainException>(() => ticket.AddComment(stranger, "Hello", false, Now));
        Assert.Throws<DomainException>(() => ticket.Assign(stranger, stranger.Id, Now));
        Assert.Throws<DomainException>(() => ticket.Edit(stranger, "Other", "Other", TicketPriority.Low, Now));
        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Empty(ticket.Comments);
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
    public void A_due_date_chosen_when_opening_is_kept()
    {
        var ticket = Ticket.Create(1, "Printer is down", "", TicketPriority.Low, Customer, Now, new DateOnly(2026, 3, 10));

        Assert.Equal(new DateOnly(2026, 3, 10), ticket.DueDate);
    }

    [Fact]
    public void The_due_date_can_be_today_but_not_in_the_past()
    {
        Assert.Equal(new DateOnly(2026, 3, 2), Ticket.Create(1, "Today", "", TicketPriority.Low, Customer, Now, new DateOnly(2026, 3, 2)).DueDate);
        Assert.Throws<DomainException>(() => Ticket.Create(1, "Yesterday", "", TicketPriority.Low, Customer, Now, new DateOnly(2026, 3, 1)));
        Assert.Throws<DomainException>(() => NewTicket().SetDueDate(Operator, new DateOnly(2026, 3, 1), Now));
    }

    [Fact]
    public void A_new_priority_leaves_the_due_date_alone()
    {
        var ticket = NewTicket(TicketPriority.Low);
        ticket.SetDueDate(Customer, new DateOnly(2026, 3, 20), Now);

        ticket.Edit(Operator, ticket.Subject, ticket.Description, TicketPriority.Urgent, Now.AddHours(1));

        Assert.Equal(new DateOnly(2026, 3, 20), ticket.DueDate);
        Assert.Equal("Priority", ticket.History[^1].Field);
    }

    [Fact]
    public void Staff_and_the_customer_set_change_and_remove_the_due_date_and_each_change_is_recorded()
    {
        var ticket = NewTicket();

        ticket.SetDueDate(Customer, new DateOnly(2026, 3, 9), Now);
        ticket.SetDueDate(Operator, new DateOnly(2026, 3, 12), Now.AddHours(1));
        ticket.SetDueDate(Admin, null, Now.AddHours(2));

        Assert.Null(ticket.DueDate);
        Assert.Equal(Now.AddHours(2), ticket.UpdatedAt);
        Assert.Equal(
            [(null, "2026-03-09"), ("2026-03-09", "2026-03-12"), ("2026-03-12", null)],
            ticket.History.Where(h => h.Field == "Due date").Select(h => (h.OldValue, h.NewValue)));
    }

    [Fact]
    public void The_same_due_date_again_records_nothing()
    {
        var ticket = NewTicket();
        ticket.SetDueDate(Customer, new DateOnly(2026, 3, 9), Now);

        ticket.SetDueDate(Customer, new DateOnly(2026, 3, 9), Now.AddHours(1));
        ticket.SetDueDate(Operator, new DateOnly(2026, 3, 9), Now.AddDays(30));

        Assert.Single(ticket.History, h => h.Field == "Due date");
        Assert.Equal(Now, ticket.UpdatedAt);
    }

    [Fact]
    public void Nobody_else_sets_the_due_date_and_not_on_a_closed_ticket()
    {
        var ticket = NewTicket();

        Assert.Throws<DomainException>(() => ticket.SetDueDate(OtherCustomer, new DateOnly(2026, 3, 9), Now));
        Assert.Throws<DomainException>(() => ticket.SetDueDate(Operator with { WorkspaceId = OtherWorkspace }, new DateOnly(2026, 3, 9), Now));

        ticket.ChangeStatus(Customer, TicketStatus.Closed, Now);
        Assert.Throws<DomainException>(() => ticket.SetDueDate(Operator, new DateOnly(2026, 3, 9), Now));
    }

    [Fact]
    public void Only_staff_can_make_a_ticket_urgent()
    {
        var ticket = NewTicket();

        Assert.Throws<DomainException>(() => ticket.Edit(Customer, ticket.Subject, ticket.Description, TicketPriority.Urgent, Now));
    }

    [Fact]
    public void The_customer_edits_the_subject_description_and_priority_and_each_change_is_recorded()
    {
        var ticket = NewTicket();

        ticket.Edit(Customer, "  Printer jams  ", "Paper gets stuck.", TicketPriority.Backlog, Now.AddHours(2));

        Assert.Equal("Printer jams", ticket.Subject);
        Assert.Equal("Paper gets stuck.", ticket.Description);
        Assert.Equal(TicketPriority.Backlog, ticket.Priority);
        Assert.Equal(Now.AddHours(2), ticket.UpdatedAt);
        Assert.Equal(["Subject", "Description", "Priority"], ticket.History.Skip(1).Select(h => h.Field));
        Assert.Equal(("Printer is down", "Printer jams"), (ticket.History[1].OldValue, ticket.History[1].NewValue));
        Assert.Null(ticket.History[2].NewValue);
    }

    [Fact]
    public void Saving_without_changes_records_nothing()
    {
        var ticket = NewTicket();

        ticket.Edit(Customer, ticket.Subject, ticket.Description, ticket.Priority, Now.AddHours(1));

        Assert.Single(ticket.History);
        Assert.Equal(Now, ticket.UpdatedAt);
    }

    [Fact]
    public void A_customer_keeps_an_urgent_ticket_urgent_while_editing_it()
    {
        var ticket = NewTicket(TicketPriority.Urgent);

        ticket.Edit(Customer, "New subject", ticket.Description, TicketPriority.Urgent, Now);

        Assert.Equal("New subject", ticket.Subject);
    }

    [Fact]
    public void Only_the_customer_who_opened_it_and_staff_may_edit()
    {
        var ticket = NewTicket();

        Assert.Throws<DomainException>(() => ticket.Edit(OtherCustomer, "Mine now", ticket.Description, ticket.Priority, Now));
        ticket.Edit(Admin, "Edited by staff", ticket.Description, ticket.Priority, Now);
        Assert.Equal("Edited by staff", ticket.Subject);
    }

    [Fact]
    public void An_edit_needs_a_subject()
    {
        var ticket = NewTicket();

        Assert.Throws<DomainException>(() => ticket.Edit(Customer, " ", ticket.Description, ticket.Priority, Now));
    }

    [Fact]
    public void An_edit_may_erase_the_description()
    {
        var ticket = NewTicket();

        ticket.Edit(Customer, ticket.Subject, " ", ticket.Priority, Now);

        Assert.Equal(string.Empty, ticket.Description);
        Assert.Equal("Description", ticket.History[^1].Field);
    }

    [Fact]
    public void Closed_ticket_cannot_be_assigned_commented_or_edited()
    {
        var ticket = NewTicket();
        ticket.ChangeStatus(Customer, TicketStatus.Closed, Now);

        Assert.Throws<DomainException>(() => ticket.Assign(Operator, Operator.Id, Now));
        Assert.Throws<DomainException>(() => ticket.AddComment(Customer, "hi", false, Now));
        Assert.Throws<DomainException>(() => ticket.Edit(Operator, "New subject", ticket.Description, TicketPriority.High, Now));
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
    public void An_open_ticket_is_overdue_from_the_day_after_its_due_date()
    {
        var ticket = NewTicket(TicketPriority.Urgent);
        ticket.SetDueDate(Operator, new DateOnly(2026, 3, 3), Now);

        Assert.False(ticket.IsOverdue(new DateTimeOffset(2026, 3, 3, 23, 59, 0, TimeSpan.Zero)));
        Assert.True(ticket.IsOverdue(new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void A_ticket_without_a_due_date_is_never_overdue()
        => Assert.False(NewTicket().IsOverdue(Now.AddYears(1)));

    [Fact]
    public void A_closed_ticket_is_judged_by_the_day_it_was_closed()
    {
        var onTime = NewTicket();
        onTime.SetDueDate(Operator, new DateOnly(2026, 3, 3), Now);
        onTime.ChangeStatus(Customer, TicketStatus.Closed, Now.AddDays(1));

        var late = NewTicket();
        late.SetDueDate(Operator, new DateOnly(2026, 3, 3), Now);
        late.ChangeStatus(Customer, TicketStatus.Closed, Now.AddDays(2));

        Assert.False(onTime.IsOverdue(Now.AddDays(30)));
        Assert.True(late.IsOverdue(Now.AddDays(30)));
    }
}

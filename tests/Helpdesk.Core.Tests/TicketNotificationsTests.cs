using Helpdesk.Core.Notifications;
using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;

namespace Helpdesk.Core.Tests;

public class TicketNotificationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    private static readonly Actor Customer = new("customer-1", UserRole.Customer, 1);
    private static readonly Actor Operator = new("operator-1", UserRole.Operator, 1);
    private static readonly Actor OtherOperator = new("operator-2", UserRole.Operator, 1);

    private static Ticket NewTicket(Actor? assignee = null)
    {
        var ticket = Ticket.Create(1, "Printer is down", "It prints blank pages.", TicketPriority.Normal, Customer, Now);

        if (assignee is not null)
        {
            ticket.Assign(assignee, assignee.Id, Now);
        }

        return ticket;
    }

    private static IReadOnlyList<TicketNotice> After(Ticket ticket, Actor actor, Action change)
    {
        var before = TicketNotifications.Snapshot(ticket);
        change();
        return TicketNotifications.ForChange(ticket, actor, before);
    }

    [Fact]
    public void A_reply_of_staff_reaches_the_customer_and_not_the_one_who_wrote_it()
    {
        var ticket = NewTicket(Operator);

        var notices = After(ticket, Operator, () => ticket.AddComment(Operator, "Try another cable.", false, Now));

        Assert.Equal([new TicketNotice(Customer.Id, NotificationKind.Comment)], notices);
    }

    [Fact]
    public void A_reply_of_the_customer_reaches_the_assignee_once_although_it_also_moves_the_status()
    {
        var ticket = NewTicket(Operator);
        ticket.ChangeStatus(Operator, TicketStatus.InProgress, Now);
        ticket.ChangeStatus(Operator, TicketStatus.WaitingForCustomer, Now);

        var notices = After(ticket, Customer, () => ticket.AddComment(Customer, "Still broken.", false, Now));

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal([new TicketNotice(Operator.Id, NotificationKind.Comment)], notices);
    }

    [Fact]
    public void An_internal_note_reaches_the_assignee_but_never_the_customer()
    {
        var ticket = NewTicket(Operator);

        var notices = After(ticket, OtherOperator, () => ticket.AddComment(OtherOperator, "Known issue.", true, Now));

        Assert.Equal([new TicketNotice(Operator.Id, NotificationKind.InternalNote)], notices);
    }

    [Fact]
    public void Nobody_hears_of_a_reply_on_a_ticket_without_an_assignee_but_the_customer()
    {
        var ticket = NewTicket();

        Assert.Empty(After(ticket, Customer, () => ticket.AddComment(Customer, "Any news?", false, Now)));
        Assert.Empty(After(ticket, Operator, () => ticket.AddComment(Operator, "Note to self.", true, Now)));
    }

    [Fact]
    public void A_new_status_reaches_the_customer_and_the_assignee_with_the_status()
    {
        var ticket = NewTicket(Operator);

        var notices = After(ticket, OtherOperator, () => ticket.ChangeStatus(OtherOperator, TicketStatus.InProgress, Now));

        Assert.Equal(
            [
                new TicketNotice(Customer.Id, NotificationKind.StatusChanged, "InProgress"),
                new TicketNotice(Operator.Id, NotificationKind.StatusChanged, "InProgress"),
            ],
            notices);
    }

    [Fact]
    public void A_customer_closing_with_a_reason_is_told_as_the_closing()
    {
        var ticket = NewTicket(Operator);

        var notices = After(ticket, Customer, () => ticket.ChangeStatus(Customer, TicketStatus.Closed, Now, "Works now."));

        Assert.Equal([new TicketNotice(Operator.Id, NotificationKind.StatusChanged, "Closed")], notices);
    }

    [Fact]
    public void Giving_a_ticket_to_someone_tells_them_but_taking_it_oneself_does_not()
    {
        var ticket = NewTicket();

        Assert.Equal(
            [new TicketNotice(OtherOperator.Id, NotificationKind.Assigned)],
            After(ticket, Operator, () => ticket.Assign(Operator, OtherOperator.Id, Now)));
        Assert.Empty(After(ticket, Operator, () => ticket.Assign(Operator, Operator.Id, Now)));
        Assert.Empty(After(ticket, Operator, () => ticket.Assign(Operator, null, Now)));
    }

    [Fact]
    public void Editing_the_text_or_the_due_date_tells_nobody()
    {
        var ticket = NewTicket(Operator);

        Assert.Empty(After(ticket, Customer, () => ticket.Edit(Customer, "Printer jams", "Paper gets stuck.", TicketPriority.High, Now)));
        Assert.Empty(After(ticket, Customer, () => ticket.SetDueDate(Customer, new DateOnly(2026, 3, 9), Now)));
    }
}

using System.Net;
using Helpdesk.Core.Tickets;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

public class TicketTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private static Dictionary<string, string> Fields(params (string Key, string Value)[] pairs)
        => pairs.ToDictionary(p => p.Key, p => p.Value);

    private static async Task<string> FollowedHtmlAsync(AppClient client, HttpResponseMessage redirect)
    {
        var page = await client.FollowAsync(redirect);
        return await page.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Customer_creates_a_ticket_and_sees_it_in_the_list()
    {
        var customer = await SignedInAsync("Customer");

        var id = await customer.CreateTicketAsync("Printer is down", priority: "High");

        var list = await customer.GetHtmlAsync("/Tickets");
        Assert.Contains("Printer is down", list);
        var details = await customer.GetHtmlAsync($"/Tickets/Details/{id}");
        Assert.Contains("Printer is down", details);
        Assert.Contains("High", details);
    }

    [Fact]
    public async Task Ticket_without_a_subject_is_not_created()
    {
        var customer = await SignedInAsync("Customer");

        var response = await customer.PostFormAsync("/Tickets/Create", "/Tickets/Create", Fields(
            ("Input.Subject", ""), ("Input.Description", "text"), ("Input.Priority", "Normal")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_ticket_can_be_created_without_a_description_and_it_can_be_erased_later()
    {
        var customer = await SignedInAsync("Customer");

        var response = await customer.PostFormAsync("/Tickets/Create", "/Tickets/Create", Fields(
            ("Input.Subject", "Just the subject"), ("Input.Description", ""), ("Input.Priority", "Normal")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var number = int.Parse(response.Headers.Location!.OriginalString.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("Just the subject", await customer.GetHtmlAsync($"/Tickets/Details/{number}"));

        var other = await customer.CreateTicketAsync("With a text", "Some words to erase.");
        await customer.PostTicketActionAsync(other, "Description", Fields(("description", "")));
        Assert.DoesNotContain("Some words to erase.", await customer.GetHtmlAsync($"/Tickets/Details/{other}"));
    }

    private static string DayValue(int daysFromToday)
        => DateTime.UtcNow.Date.AddDays(daysFromToday).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string DueField(string value) => $"id=\"dueDate\" type=\"date\" name=\"dueDate\" value=\"{value}\"";

    [Fact]
    public async Task A_due_date_is_there_only_when_one_was_chosen_and_never_a_past_one()
    {
        var customer = await SignedInAsync("Customer");

        var dated = await customer.PostFormAsync("/Tickets/Create", "/Tickets/Create", Fields(
            ("Input.Subject", "With a date"), ("Input.Priority", "Normal"), ("Input.DueDate", DayValue(400))));
        var past = await customer.PostFormAsync("/Tickets/Create", "/Tickets/Create", Fields(
            ("Input.Subject", "In the past"), ("Input.Priority", "Normal"), ("Input.DueDate", "2020-01-01")));
        var undated = await customer.CreateTicketAsync("Without a date");

        Assert.Equal(HttpStatusCode.Redirect, dated.StatusCode);
        Assert.Contains(DueField(DayValue(400)), await (await customer.FollowAsync(dated)).Content.ReadAsStringAsync());
        Assert.Contains("The due date cannot be in the past", await past.Content.ReadAsStringAsync());
        Assert.Contains(DueField(string.Empty), await customer.GetHtmlAsync($"/Tickets/Details/{undated}"));
    }

    [Fact]
    public async Task The_customer_and_staff_change_the_due_date_on_the_ticket_page()
    {
        var customer = await SignedInAsync("Customer");
        var number = await customer.CreateTicketAsync("Needs a date");
        var op = await SignedInAsync("Operator");

        await customer.PostTicketActionAsync(number, "DueDate", Fields(("dueDate", DayValue(5))));
        Assert.Contains(DueField(DayValue(5)), await op.GetHtmlAsync($"/Tickets/Details/{number}"));

        await op.PostTicketActionAsync(number, "DueDate", Fields(("dueDate", DayValue(3))));
        var past = await op.PostTicketActionAsync(number, "DueDate", Fields(("dueDate", "2020-01-01")));
        Assert.Contains("The due date cannot be in the past", await FollowedHtmlAsync(op, past));
        var garbage = await op.PostTicketActionAsync(number, "DueDate", Fields(("dueDate", "next week")));
        Assert.Contains("Choose the due date in the calendar", await FollowedHtmlAsync(op, garbage));
        var page = await op.GetHtmlAsync($"/Tickets/Details/{number}");
        Assert.Contains(DueField(DayValue(3)), page);
        Assert.Contains("Due date: none &rarr; ", page);

        await customer.PostTicketActionAsync(number, "DueDate", Fields(("dueDate", "")));
        Assert.Contains(DueField(string.Empty), await customer.GetHtmlAsync($"/Tickets/Details/{number}"));
    }

    [Fact]
    public async Task Customer_cannot_open_an_urgent_ticket()
    {
        var customer = await SignedInAsync("Customer");

        var response = await customer.PostFormAsync("/Tickets/Create", "/Tickets/Create", Fields(
            ("Input.Subject", "Urgent please"), ("Input.Description", "text"), ("Input.Priority", "Urgent")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Only staff can set urgent priority", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Customer_cannot_see_the_ticket_of_another_customer()
    {
        var owner = await SignedInAsync("Customer");
        var id = await owner.CreateTicketAsync("Private matter");

        await CreateUserAsync("Customer", "other@example.test");
        var other = NewClient();
        await other.SignInAsync("other@example.test", TestPassword);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/Tickets/Details/{id}")).StatusCode);
        Assert.DoesNotContain("Private matter", await other.GetHtmlAsync("/Tickets"));
    }

    [Fact]
    public async Task Staff_sees_tickets_of_all_customers()
    {
        var customer = await SignedInAsync("Customer");
        await customer.CreateTicketAsync("Needs help");

        var op = await SignedInAsync("Operator");

        Assert.Contains("Needs help", await op.GetHtmlAsync("/Tickets"));
    }

    [Fact]
    public async Task Internal_notes_and_history_are_hidden_from_the_customer()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Question");
        var op = await SignedInAsync("Operator");

        await op.PostTicketActionAsync(id, "Comment", Fields(("body", "secret remark"), ("isInternal", "true")));
        await op.PostTicketActionAsync(id, "Comment", Fields(("body", "public answer")));

        var customerView = await customer.GetHtmlAsync($"/Tickets/Details/{id}");
        Assert.Contains("public answer", customerView);
        Assert.DoesNotContain("secret remark", customerView);
        Assert.DoesNotContain(">History<", customerView);

        var staffView = await op.GetHtmlAsync($"/Tickets/Details/{id}");
        Assert.Contains("secret remark", staffView);
        Assert.Contains(">History<", staffView);
    }

    [Fact]
    public async Task The_history_sits_between_the_attachments_and_the_comments()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Ordered page");
        var op = await SignedInAsync("Operator");

        var html = await op.GetHtmlAsync($"/Tickets/Details/{id}");

        var attachments = html.IndexOf(">Attachments<", StringComparison.Ordinal);
        var history = html.IndexOf(">History<", StringComparison.Ordinal);
        var comments = html.IndexOf(">Comments<", StringComparison.Ordinal);
        Assert.True(attachments < history && history < comments, "Expected attachments, then history, then comments.");
        Assert.Contains("class=\"timeline\" data-show-last=\"5\"", html);
        Assert.Contains("class=\"comment-list\" data-show-last=\"5\"", html);
    }

    [Fact]
    public async Task Operator_moves_a_ticket_through_the_workflow()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Workflow");
        var op = await SignedInAsync("Operator");

        await op.PostTicketActionAsync(id, "Status", Fields(("status", "InProgress")));
        Assert.Contains("In progress", await op.GetHtmlAsync($"/Tickets/Details/{id}"));

        await op.PostTicketActionAsync(id, "Status", Fields(("status", "Closed")));
        var closed = await op.GetHtmlAsync($"/Tickets/Details/{id}");
        Assert.Contains("Closed", closed);
        Assert.DoesNotContain("Add a comment", closed);
    }

    [Fact]
    public async Task Customer_cannot_start_work_on_a_ticket()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Sneaky");

        var response = await customer.PostTicketActionAsync(id, "Status", Fields(("status", "InProgress")));

        var html = await FollowedHtmlAsync(customer, response);
        Assert.Contains("is not allowed", html);
        Assert.Contains("pill pill-new", await customer.GetHtmlAsync($"/Tickets/Details/{id}"));
    }

    [Fact]
    public async Task Status_priority_and_assignee_are_changed_on_the_ticket_page_without_an_apply_button()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Pick me");
        var op = await SignedInAsync("Operator");

        var before = await op.GetHtmlAsync($"/Tickets/Details/{id}");
        Assert.DoesNotContain(">Apply<", before);
        Assert.Contains("name=\"status\" value=\"New\" checked=\"checked\"", before);
        Assert.Contains("name=\"priority\" value=\"Normal\" checked=\"checked\"", before);

        await op.PostTicketActionAsync(id, "Priority", Fields(("priority", "High")));
        await op.PostTicketActionAsync(id, "Status", Fields(("status", "InProgress")));

        var after = await op.GetHtmlAsync($"/Tickets/Details/{id}");
        Assert.Contains("name=\"priority\" value=\"High\" checked=\"checked\"", after);
        Assert.Contains("name=\"status\" value=\"InProgress\" checked=\"checked\"", after);
        Assert.Contains("Priority: Normal &rarr; High", after);
    }

    [Fact]
    public async Task A_priority_that_does_not_exist_is_refused()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Odd value");

        var response = await customer.PostTicketActionAsync(id, "Priority", Fields(("priority", "Whenever")));

        Assert.Contains("Choose a priority", await FollowedHtmlAsync(customer, response));
        Assert.Contains("name=\"priority\" value=\"Normal\" checked=\"checked\"", await customer.GetHtmlAsync($"/Tickets/Details/{id}"));
    }

    [Fact]
    public async Task Customer_cannot_assign_or_make_a_ticket_urgent()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Mine");

        var assign = await customer.PostTicketActionAsync(id, "Assign", Fields(("assigneeId", await StandardUserIdAsync("Operator"))));
        Assert.Contains("Only staff", await FollowedHtmlAsync(customer, assign));

        var urgent = await customer.PostTicketActionAsync(id, "Priority", Fields(("priority", "Urgent")));
        Assert.Contains("Only staff can set urgent priority", await FollowedHtmlAsync(customer, urgent));
        Assert.DoesNotContain("value=\"Urgent\"", await customer.GetHtmlAsync($"/Tickets/Details/{id}"));
    }

    [Fact]
    public async Task Operator_assigns_the_ticket_to_staff_only()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Assign me");
        var op = await SignedInAsync("Operator");

        var ok = await op.PostTicketActionAsync(id, "Assign", Fields(("assigneeId", await StandardUserIdAsync("Operator"))));
        Assert.Contains("Test operator", await FollowedHtmlAsync(op, ok));

        var bad = await op.PostTicketActionAsync(id, "Assign", Fields(("assigneeId", await StandardUserIdAsync("Customer"))));
        Assert.Contains("active operators and admins of this help desk only", await FollowedHtmlAsync(op, bad));
    }

    [Fact]
    public async Task Customer_reply_puts_a_waiting_ticket_back_in_progress()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Waiting");
        var op = await SignedInAsync("Operator");
        await op.PostTicketActionAsync(id, "Status", Fields(("status", "InProgress")));
        await op.PostTicketActionAsync(id, "Status", Fields(("status", "WaitingForCustomer")));
        Assert.Contains("Waiting for customer", await customer.GetHtmlAsync($"/Tickets/Details/{id}"));

        await customer.PostTicketActionAsync(id, "Comment", Fields(("body", "Here are the details")));

        var html = await customer.GetHtmlAsync($"/Tickets/Details/{id}");
        Assert.Contains("pill pill-progress", html);
        Assert.DoesNotContain("pill pill-waiting\">Waiting", html);
    }

    [Fact]
    public async Task Each_change_says_how_it_went_on_the_page_drawn_next_and_only_there()
    {
        var customer = await SignedInAsync("Customer");
        var created = await customer.PostFormAsync("/Tickets/Create", "/Tickets/Create", Fields(
            ("Input.Subject", "Tell me how it went"), ("Input.Priority", "Normal")));
        var id = int.Parse(created.Headers.Location!.OriginalString.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);

        var first = await FollowedHtmlAsync(customer, created);
        Assert.Contains("result-toast-success", first);
        Assert.Contains($"Ticket #{id} created.", first);
        Assert.DoesNotContain($"Ticket #{id} created.", await customer.GetHtmlAsync($"/Tickets/Details/{id}"));

        var op = await SignedInAsync("Operator");
        var day = DateTime.UtcNow.Date.AddDays(3).ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains($"Status of #{id} changed to In progress.", await FollowedHtmlAsync(op,
            await op.PostTicketActionAsync(id, "Status", Fields(("status", "InProgress")))));
        Assert.Contains($"Priority of #{id} changed to High.", await FollowedHtmlAsync(op,
            await op.PostTicketActionAsync(id, "Priority", Fields(("priority", "High")))));
        Assert.Contains($"Due date of #{id} set to {day}.", await FollowedHtmlAsync(op,
            await op.PostTicketActionAsync(id, "DueDate", Fields(("dueDate", DayValue(3))))));
        Assert.Contains($"Comment added to #{id}.", await FollowedHtmlAsync(op,
            await op.PostTicketActionAsync(id, "Comment", Fields(("body", "On it")))));

        var refused = await FollowedHtmlAsync(op, await op.PostTicketActionAsync(id, "Priority", Fields()));
        Assert.Contains("result-toast-failure", refused);
        Assert.Contains("Choose a priority.", refused);
        Assert.DoesNotContain("result-toast-success", refused);
    }

    [Fact]
    public async Task Stale_page_cannot_overwrite_a_newer_change()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Concurrent");
        var op = await SignedInAsync("Operator");
        var staleVersion = AppClient.ExtractRowVersion(await op.GetHtmlAsync($"/Tickets/Details/{id}"));

        await op.PostTicketActionAsync(id, "Comment", Fields(("body", "first change")));
        var stale = await op.PostTicketActionAsync(id, "Priority", Fields(("priority", "Low")), staleVersion);

        Assert.Contains("changed by someone else", await FollowedHtmlAsync(op, stale));
        Assert.Contains("name=\"priority\" value=\"Normal\" checked=\"checked\"", await op.GetHtmlAsync($"/Tickets/Details/{id}"));
    }

    [Fact]
    public async Task Changes_are_stored_with_history_and_leave_the_due_date_empty()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Audited", priority: "High");
        var op = await SignedInAsync("Operator");
        await op.PostTicketActionAsync(id, "Status", Fields(("status", "InProgress")));
        await op.PostTicketActionAsync(id, "Priority", Fields(("priority", "Urgent")));

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var ticket = await db.Tickets.IgnoreQueryFilters().Include(t => t.History).SingleAsync(t => t.WorkspaceId == SharedWorkspace.Id && t.Number == id);

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal(TicketPriority.Urgent, ticket.Priority);
        Assert.Null(ticket.DueDate);
        Assert.Equal(["Created", "Status", "Priority"], ticket.History.OrderBy(h => h.Id).Select(h => h.Field));
        Assert.All(ticket.History, h => Assert.False(string.IsNullOrEmpty(h.ActorId)));
    }

    [Fact]
    public async Task The_customer_edits_the_subject_and_description_on_the_ticket_page()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Printer is down", "It prints blank pages.");

        var page = await customer.GetHtmlAsync($"/Tickets/Details/{id}");
        Assert.Contains("handler=Subject", page);
        Assert.Contains("handler=Description", page);

        var subject = await customer.PostTicketActionAsync(id, "Subject", Fields(("subject", "Printer jams")));
        var description = await customer.PostTicketActionAsync(id, "Description", Fields(("description", "Paper gets stuck.")));

        Assert.Equal(HttpStatusCode.Redirect, subject.StatusCode);
        Assert.Equal($"/Tickets/Details/{id}", subject.Headers.Location!.OriginalString);
        var after = await customer.GetHtmlAsync($"/Tickets/Details/{id}");
        Assert.Contains("Printer jams", after);
        Assert.Contains("Paper gets stuck.", after);
        Assert.DoesNotContain("It prints blank pages.", after);

        var staffView = await (await SignedInAsync("Operator")).GetHtmlAsync($"/Tickets/Details/{id}");
        Assert.Contains("Subject: Printer is down &rarr; Printer jams", staffView);
        Assert.Contains("Description edited", staffView);
    }

    [Fact]
    public async Task An_empty_subject_is_refused_and_the_old_one_kept()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Keep this subject");

        var response = await customer.PostTicketActionAsync(id, "Subject", Fields(("subject", "   ")));

        Assert.Contains("Subject is required", await FollowedHtmlAsync(customer, response));
        Assert.Contains("Keep this subject", await customer.GetHtmlAsync($"/Tickets/Details/{id}"));
    }

    [Fact]
    public async Task Staff_can_make_a_ticket_urgent()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Server room is flooding");
        var op = await SignedInAsync("Operator");

        var saved = await op.PostTicketActionAsync(id, "Priority", Fields(("priority", "Urgent")));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("name=\"priority\" value=\"Urgent\" checked=\"checked\"", await customer.GetHtmlAsync($"/Tickets/Details/{id}"));
    }

    [Fact]
    public async Task Another_customer_cannot_edit_the_ticket()
    {
        var owner = await SignedInAsync("Customer");
        var id = await owner.CreateTicketAsync("Not yours");
        await CreateUserAsync("Customer", "zoe@example.test");
        var zoe = NewClient();
        await zoe.SignInAsync("zoe@example.test", TestPassword);

        var post = await zoe.PostFormAsync("/Tickets/Create", $"/Tickets/Details/{id}?handler=Subject", Fields(
            ("rowVersion", "AAAAAAAAB9E="), ("subject", "Taken over")));

        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Contains("Not yours", await owner.GetHtmlAsync($"/Tickets/Details/{id}"));
    }

    [Fact]
    public async Task A_customer_closes_a_ticket_only_with_a_reason_that_staff_see_in_the_comments()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("No longer needed");
        Assert.Contains("data-close-reason=\"required\"", await customer.GetHtmlAsync($"/Tickets/Details/{id}"));

        var refused = await customer.PostTicketActionAsync(id, "Status", Fields(("status", "Closed")));
        Assert.Contains("Say why the ticket is being closed", await FollowedHtmlAsync(customer, refused));

        await customer.PostTicketActionAsync(id, "Status", Fields(("status", "Closed"), ("reason", "Found the answer in the manual.")));

        var op = await SignedInAsync("Operator");
        var page = await op.GetHtmlAsync($"/Tickets/Details/{id}");
        Assert.Contains("pill pill-closed", page);
        Assert.Contains("Found the answer in the manual.", page);
        Assert.Contains("Reason for closing", page);
        Assert.DoesNotContain("data-close-reason=\"required\"", page);
    }

    [Fact]
    public async Task A_closed_ticket_cannot_be_edited()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Done");
        await customer.PostTicketActionAsync(id, "Status", Fields(("status", "Closed"), ("reason", "Sorted it out myself.")));

        // Staff still see the ticket's version, in the form that reopens it.
        var op = await SignedInAsync("Operator");
        var details = await op.GetHtmlAsync($"/Tickets/Details/{id}");
        var edit = await op.PostTicketActionAsync(id, "Subject", Fields(("subject", "Changed after all")));

        Assert.DoesNotContain("handler=Subject", details);
        Assert.DoesNotContain("handler=Subject", await customer.GetHtmlAsync($"/Tickets/Details/{id}"));
        Assert.Contains("The ticket is closed", await FollowedHtmlAsync(op, edit));
        Assert.DoesNotContain("Changed after all", await customer.GetHtmlAsync($"/Tickets/Details/{id}"));
    }
}

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
        Assert.Contains("badge text-bg-primary", await customer.GetHtmlAsync($"/Tickets/Details/{id}"));
    }

    [Fact]
    public async Task Customer_cannot_assign_or_reprioritize()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Mine");

        var assign = await customer.PostTicketActionAsync(id, "Assign", Fields(("assigneeId", await DemoUserIdAsync("Operator"))));
        Assert.Contains("Only staff", await FollowedHtmlAsync(customer, assign));

        var priority = await customer.PostTicketActionAsync(id, "Priority", Fields(("priority", "Urgent")));
        Assert.Contains("Only staff", await FollowedHtmlAsync(customer, priority));
    }

    [Fact]
    public async Task Operator_assigns_the_ticket_to_staff_only()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Assign me");
        var op = await SignedInAsync("Operator");

        var ok = await op.PostTicketActionAsync(id, "Assign", Fields(("assigneeId", await DemoUserIdAsync("Operator"))));
        Assert.Contains("Demo operator", await FollowedHtmlAsync(op, ok));

        var bad = await op.PostTicketActionAsync(id, "Assign", Fields(("assigneeId", await DemoUserIdAsync("Customer"))));
        Assert.Contains("active operators and admins only", await FollowedHtmlAsync(op, bad));
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
        Assert.Contains("badge text-bg-info", html);
        Assert.DoesNotContain("badge text-bg-warning\">Waiting", html);
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
        Assert.DoesNotContain("badge text-bg-light border\">Low", await op.GetHtmlAsync($"/Tickets/Details/{id}"));
    }

    [Fact]
    public async Task Changes_are_stored_with_history_and_sla_deadline()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Audited", priority: "High");
        var op = await SignedInAsync("Operator");
        await op.PostTicketActionAsync(id, "Status", Fields(("status", "InProgress")));
        await op.PostTicketActionAsync(id, "Priority", Fields(("priority", "Urgent")));

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var ticket = await db.Tickets.Include(t => t.History).SingleAsync(t => t.Id == id);

        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal(TicketPriority.Urgent, ticket.Priority);
        Assert.Equal(ticket.CreatedAt.AddHours(4), ticket.DueAt);
        Assert.Equal(["Created", "Status", "Priority"], ticket.History.OrderBy(h => h.Id).Select(h => h.Field));
        Assert.All(ticket.History, h => Assert.False(string.IsNullOrEmpty(h.ActorId)));
    }
}

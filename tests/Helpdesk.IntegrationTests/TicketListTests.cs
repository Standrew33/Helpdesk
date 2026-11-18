using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

public partial class TicketListTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private static int RowCount(string html) => RowPattern().Count(html);

    [GeneratedRegex(@"class=""ticket-subject""")]
    private static partial Regex RowPattern();

    private static async Task<int> CreateManyAsync(AppClient customer, int count)
    {
        var last = 0;

        for (var i = 1; i <= count; i++)
        {
            last = await customer.CreateTicketAsync($"Bulk ticket {i:00}");
        }

        return last;
    }

    [Fact]
    public async Task Every_row_opens_its_ticket_from_the_icon_at_its_end_and_on_a_double_click()
    {
        var customer = await SignedInAsync("Customer");
        var number = await customer.CreateTicketAsync("Open me");

        var list = await customer.GetHtmlAsync("/Tickets");

        Assert.Contains($"<tr data-open=\"/Tickets/Details/{number}\">", list);
        Assert.Contains($"aria-label=\"Open ticket #{number}\"", list);
        Assert.Contains("<span class=\"ticket-subject\">Open me</span>", list);
    }

    [Fact]
    public async Task Long_lists_are_split_into_pages()
    {
        var customer = await SignedInAsync("Customer");
        await CreateManyAsync(customer, 17);

        var first = await customer.GetHtmlAsync("/Tickets");
        var second = await customer.GetHtmlAsync("/Tickets?p=2");

        Assert.Equal(15, RowCount(first));
        Assert.Contains("Page 1 of 2", first);
        Assert.Contains("17 tickets", first);
        Assert.Equal(2, RowCount(second));
        Assert.Contains("Page 2 of 2", second);
    }

    [Fact]
    public async Task The_newest_activity_comes_first_and_pages_do_not_overlap()
    {
        var customer = await SignedInAsync("Customer");
        await CreateManyAsync(customer, 17);

        var first = await customer.GetHtmlAsync("/Tickets");
        var second = await customer.GetHtmlAsync("/Tickets?p=2");

        Assert.Contains("Bulk ticket 17", first);
        Assert.DoesNotContain("Bulk ticket 17", second);
        Assert.Contains("Bulk ticket 01", second);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    [InlineData(99)]
    public async Task A_page_number_out_of_range_shows_the_nearest_page(int page)
    {
        var customer = await SignedInAsync("Customer");
        await CreateManyAsync(customer, 17);

        var response = await customer.GetAsync($"/Tickets?p={page}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(0, RowCount(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Search_finds_tickets_by_subject_and_by_description()
    {
        var customer = await SignedInAsync("Customer");
        await customer.CreateTicketAsync("Printer is jammed", "Paper everywhere.");
        await customer.CreateTicketAsync("Cannot log in", "The VPN token has expired.");

        var bySubject = await customer.GetHtmlAsync("/Tickets?q=printer");
        var byDescription = await customer.GetHtmlAsync("/Tickets?q=token");

        Assert.Contains("Printer is jammed", bySubject);
        Assert.DoesNotContain("Cannot log in", bySubject);
        Assert.Contains("Cannot log in", byDescription);
        Assert.DoesNotContain("Printer is jammed", byDescription);
    }

    [Fact]
    public async Task Search_finds_a_ticket_by_its_number()
    {
        var customer = await SignedInAsync("Customer");
        await customer.CreateTicketAsync("First one");
        var wanted = await customer.CreateTicketAsync("Second one");
        await customer.CreateTicketAsync("Third one");

        var html = await customer.GetHtmlAsync($"/Tickets?q=%23{wanted}");

        Assert.Contains("Second one", html);
        Assert.DoesNotContain("First one", html);
        Assert.DoesNotContain("Third one", html);
    }

    [Fact]
    public async Task Search_treats_wildcards_as_plain_text()
    {
        var customer = await SignedInAsync("Customer");
        await customer.CreateTicketAsync("Ordinary request");

        var html = await customer.GetHtmlAsync("/Tickets?q=%25");

        Assert.DoesNotContain("Ordinary request", html);
        Assert.Contains("Nothing matches", html);
    }

    [Fact]
    public async Task Status_and_priority_filters_combine()
    {
        var customer = await SignedInAsync("Customer");
        await customer.CreateTicketAsync("High one", priority: "High");
        await customer.CreateTicketAsync("Low one", priority: "Low");

        var html = await customer.GetHtmlAsync("/Tickets?status=New&priority=High");
        var none = await customer.GetHtmlAsync("/Tickets?status=Closed&priority=High");

        Assert.Contains("High one", html);
        Assert.DoesNotContain("Low one", html);
        Assert.Equal(0, RowCount(none));
    }

    [Fact]
    public async Task Several_values_of_one_filter_mean_any_of_them()
    {
        var customer = await SignedInAsync("Customer");
        await customer.CreateTicketAsync("High one", priority: "High");
        await customer.CreateTicketAsync("Low one", priority: "Low");
        await customer.CreateTicketAsync("Backlog one", priority: "Backlog");

        var html = await customer.GetHtmlAsync("/Tickets?priority=High&priority=Backlog");

        Assert.Contains("High one", html);
        Assert.Contains("Backlog one", html);
        Assert.DoesNotContain("Low one", html);
    }

    [Fact]
    public async Task Staff_queue_views_can_be_combined()
    {
        var customer = await SignedInAsync("Customer");
        var taken = await customer.CreateTicketAsync("Taken ticket");
        await customer.CreateTicketAsync("Free ticket");
        var other = await customer.CreateTicketAsync("Someone else's ticket");
        var op = await SignedInAsync("Operator");
        await op.PostTicketActionAsync(taken, "Assign", new Dictionary<string, string> { ["assigneeId"] = await StandardUserIdAsync("Operator") });
        await op.PostTicketActionAsync(other, "Assign", new Dictionary<string, string> { ["assigneeId"] = await StandardUserIdAsync("Admin") });

        var html = await op.GetHtmlAsync("/Tickets?scope=Mine&scope=Unassigned");

        Assert.Contains("Taken ticket", html);
        Assert.Contains("Free ticket", html);
        Assert.DoesNotContain("Someone else", html);
    }

    private static List<string> SubjectsIn(string html)
        => SubjectPattern().Matches(html).Select(m => m.Groups[1].Value).ToList();

    [GeneratedRegex(@"class=""ticket-subject""[^>]*>([^<]+)<")]
    private static partial Regex SubjectPattern();

    [Fact]
    public async Task Priority_sorts_by_urgency_and_a_second_click_turns_it_around()
    {
        var customer = await SignedInAsync("Customer");
        await customer.CreateTicketAsync("Normal one", priority: "Normal");
        await customer.CreateTicketAsync("Backlog one", priority: "Backlog");
        await customer.CreateTicketAsync("High one", priority: "High");
        await customer.CreateTicketAsync("Low one", priority: "Low");

        var byUrgency = await customer.GetHtmlAsync("/Tickets?sort=Priority");
        var reversed = await customer.GetHtmlAsync("/Tickets?sort=Priority&dir=asc");

        Assert.Equal(["High one", "Normal one", "Low one", "Backlog one"], SubjectsIn(byUrgency));
        Assert.Equal(["Backlog one", "Low one", "Normal one", "High one"], SubjectsIn(reversed));
        Assert.Contains("aria-sort=\"descending\"", byUrgency);
        Assert.Contains("sort=Priority&amp;dir=asc", byUrgency);
    }

    [Fact]
    public async Task Sorting_keeps_the_filters()
    {
        var customer = await SignedInAsync("Customer");
        await customer.CreateTicketAsync("Bravo printer");
        await customer.CreateTicketAsync("Alpha printer");
        await customer.CreateTicketAsync("Charlie laptop");

        var html = await customer.GetHtmlAsync("/Tickets?q=printer&sort=Subject");

        Assert.Equal(["Alpha printer", "Bravo printer"], SubjectsIn(html));
        Assert.Contains("q=printer&amp;sort=Subject&amp;dir=desc", html);
        Assert.Contains("name=\"sort\" value=\"Subject\"", html);
    }

    [Fact]
    public async Task Filters_never_widen_what_a_customer_may_see()
    {
        var other = await SignedInAsync("Customer");
        await other.CreateTicketAsync("Belongs to another customer");
        await CreateUserAsync("Customer", "zoe@example.test");
        var zoe = NewClient();
        await zoe.SignInAsync("zoe@example.test", TestPassword);

        var html = await zoe.GetHtmlAsync("/Tickets?scope=Unassigned&q=another");

        Assert.DoesNotContain("Belongs to another customer", html);
    }

    [Fact]
    public async Task Staff_can_look_at_unassigned_tickets_and_their_own()
    {
        var customer = await SignedInAsync("Customer");
        var taken = await customer.CreateTicketAsync("Taken ticket");
        await customer.CreateTicketAsync("Free ticket");
        var op = await SignedInAsync("Operator");
        await op.PostTicketActionAsync(taken, "Assign", new Dictionary<string, string> { ["assigneeId"] = await StandardUserIdAsync("Operator") });

        var unassigned = await op.GetHtmlAsync("/Tickets?scope=Unassigned");
        var mine = await op.GetHtmlAsync("/Tickets?scope=Mine");

        Assert.Contains("Free ticket", unassigned);
        Assert.DoesNotContain("Taken ticket", unassigned);
        Assert.Contains("Taken ticket", mine);
        Assert.DoesNotContain("Free ticket", mine);
    }

    [Fact]
    public async Task Overdue_view_is_empty_for_fresh_tickets()
    {
        var customer = await SignedInAsync("Customer");
        await customer.CreateTicketAsync("Just created");
        var op = await SignedInAsync("Operator");

        var html = await op.GetHtmlAsync("/Tickets?scope=Overdue");

        Assert.Equal(0, RowCount(html));
    }

    private static string Day(int daysFromToday)
        => DateTime.UtcNow.Date.AddDays(daysFromToday).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<int> CreateDueAsync(AppClient customer, string subject, string? dueDate)
    {
        var response = await customer.PostFormAsync("/Tickets/Create", "/Tickets/Create", new Dictionary<string, string>
        {
            ["Input.Subject"] = subject,
            ["Input.Priority"] = "Normal",
            ["Input.DueDate"] = dueDate ?? string.Empty,
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return int.Parse(response.Headers.Location!.OriginalString.Split('/')[^1], CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Overdue_lists_open_tickets_whose_due_date_has_passed()
    {
        var customer = await SignedInAsync("Customer");
        var late = await CreateDueAsync(customer, "Late ticket", Day(0));
        await CreateDueAsync(customer, "Due today", Day(0));
        await CreateDueAsync(customer, "No due date", null);

        // Nobody can choose a day in the past, so the day goes by in the database instead.
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
            await db.Tickets.IgnoreQueryFilters().Where(t => t.Number == late)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.DueDate, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))));
        }

        var op = await SignedInAsync("Operator");
        var overdue = await op.GetHtmlAsync("/Tickets?scope=Overdue");

        Assert.Equal(["Late ticket"], SubjectsIn(overdue));
        Assert.Contains("class=\"text-nowrap overdue\"", overdue);
    }

    [Fact]
    public async Task Tickets_without_a_due_date_come_last_whichever_way_the_dates_are_sorted()
    {
        var customer = await SignedInAsync("Customer");
        await CreateDueAsync(customer, "No date", null);
        await CreateDueAsync(customer, "Later", Day(9));
        await CreateDueAsync(customer, "Sooner", Day(2));

        Assert.Equal(["Sooner", "Later", "No date"], SubjectsIn(await customer.GetHtmlAsync("/Tickets?sort=Due&dir=asc")));
        Assert.Equal(["Later", "Sooner", "No date"], SubjectsIn(await customer.GetHtmlAsync("/Tickets?sort=Due&dir=desc")));
    }

    [Fact]
    public async Task Filters_survive_paging()
    {
        var customer = await SignedInAsync("Customer");
        await CreateManyAsync(customer, 17);

        var html = await customer.GetHtmlAsync("/Tickets?q=Bulk&status=New&status=Closed");

        Assert.Contains("q=Bulk&amp;status=New&amp;status=Closed", html);
        Assert.Contains("p=2", html);
    }

    [Fact]
    public async Task Unknown_filter_values_do_not_break_the_page()
    {
        var customer = await SignedInAsync("Customer");

        var response = await customer.GetAsync("/Tickets?status=Nonsense&priority=7777&scope=Everything&p=abc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

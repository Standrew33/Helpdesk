using System.Net;
using System.Text.RegularExpressions;
using Helpdesk.IntegrationTests.Infrastructure;

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
    public async Task Filters_never_widen_what_a_customer_may_see()
    {
        var other = await SignedInAsync("Customer");
        await other.CreateTicketAsync("Belongs to the demo customer");
        await CreateUserAsync("Customer", "zoe@example.test");
        var zoe = NewClient();
        await zoe.SignInAsync("zoe@example.test", TestPassword);

        var html = await zoe.GetHtmlAsync("/Tickets?scope=Unassigned&q=demo");

        Assert.DoesNotContain("Belongs to the demo customer", html);
    }

    [Fact]
    public async Task Staff_can_look_at_unassigned_tickets_and_their_own()
    {
        var customer = await SignedInAsync("Customer");
        var taken = await customer.CreateTicketAsync("Taken ticket");
        await customer.CreateTicketAsync("Free ticket");
        var op = await SignedInAsync("Operator");
        await op.PostTicketActionAsync(taken, "Assign", new Dictionary<string, string> { ["assigneeId"] = await DemoUserIdAsync("Operator") });

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

    [Fact]
    public async Task Filters_survive_paging()
    {
        var customer = await SignedInAsync("Customer");
        await CreateManyAsync(customer, 17);

        var html = await customer.GetHtmlAsync("/Tickets?q=Bulk");

        Assert.Contains("q=Bulk", html);
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

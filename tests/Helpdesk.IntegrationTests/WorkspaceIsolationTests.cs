using System.Globalization;
using System.Net;
using System.Security.Claims;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.IntegrationTests;

/// <summary>
/// Every company has its own help desk in the same application. Whatever one workspace does, it must never see or
/// touch a ticket of another, even when it asks for a number that exists there.
/// </summary>
public class WorkspaceIsolationTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private const string FakeVersion = "AAAAAAAAB9E=";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

    /// <summary>
    /// The shared workspace gets tickets #1 and #2 (the second with a file); the other workspace gets its own #1.
    /// </summary>
    private async Task<(AppClient Customer, int OtherWorkspace)> TwoCompaniesAsync()
    {
        var customer = await SignedInAsUserAsync("Customer", "anna@company-a.test");
        await customer.CreateTicketAsync("First matter of company A");
        var second = await customer.CreateTicketAsync("Second matter of company A");
        Assert.Equal(HttpStatusCode.Redirect, (await customer.UploadAttachmentAsync(second, "plan.png", Png)).StatusCode);

        var other = await CreateWorkspaceAsync("Company B");
        var otherCustomer = await SignedInAsUserAsync("Customer", "boris@company-b.test", other);
        Assert.Equal(1, await otherCustomer.CreateTicketAsync("Only matter of company B"));

        return (customer, other);
    }

    [Theory]
    [InlineData("Operator")]
    [InlineData("Admin")]
    public async Task Staff_list_only_the_tickets_of_their_workspace(string role)
    {
        var (_, other) = await TwoCompaniesAsync();
        var stranger = await SignedInAsUserAsync(role, "staff@company-b.test", other);

        var list = await stranger.GetHtmlAsync("/Tickets");

        Assert.Contains("Only matter of company B", list);
        Assert.DoesNotContain("company A", list);
        // Ticket #2 exists only in the other workspace; text found there is not found here either.
        var byNumber = await stranger.GetHtmlAsync("/Tickets?q=%232");
        Assert.DoesNotContain("company A", byNumber);
        Assert.DoesNotContain("company B", byNumber);

        var byText = await stranger.GetHtmlAsync("/Tickets?q=matter");
        Assert.Contains("Only matter of company B", byText);
        Assert.DoesNotContain("company A", byText);
    }

    [Fact]
    public async Task The_same_number_opens_the_ticket_of_the_own_workspace()
    {
        var (_, other) = await TwoCompaniesAsync();
        var home = await SignedInAsUserAsync("Operator", "olga@company-a.test");
        var stranger = await SignedInAsUserAsync("Operator", "oleg@company-b.test", other);

        var homeView = await home.GetHtmlAsync("/Tickets/Details/1");
        var strangerView = await stranger.GetHtmlAsync("/Tickets/Details/1");

        Assert.Contains("First matter of company A", homeView);
        Assert.Contains("Only matter of company B", strangerView);
        Assert.DoesNotContain("company A", strangerView);
    }

    [Fact]
    public async Task A_number_that_exists_only_in_another_workspace_is_not_found()
    {
        var (_, other) = await TwoCompaniesAsync();
        var stranger = await SignedInAsUserAsync("Admin", "alex@company-b.test", other);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync("/Tickets/Details/2")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync("/Tickets/Details/2?handler=Download&attachmentId=1")).StatusCode);
    }

    [Theory]
    [InlineData("Comment", "body", "Written from company B")]
    [InlineData("Status", "status", "Closed")]
    [InlineData("Subject", "subject", "Renamed by company B")]
    [InlineData("Description", "description", "Rewritten by company B")]
    [InlineData("Priority", "priority", "Low")]
    [InlineData("Assign", "assigneeId", "")]
    public async Task Staff_cannot_change_a_ticket_of_another_workspace(string handler, string field, string value)
    {
        var (customer, other) = await TwoCompaniesAsync();
        var before = await customer.GetHtmlAsync("/Tickets/Details/2");
        var stranger = await SignedInAsUserAsync("Admin", "alex@company-b.test", other);

        var response = await stranger.PostFormAsync("/Tickets", $"/Tickets/Details/2?handler={handler}", new Dictionary<string, string>
        {
            ["rowVersion"] = AppClient.ExtractRowVersion(before),
            [field] = value,
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var after = await customer.GetHtmlAsync("/Tickets/Details/2");
        Assert.Equal(AppClient.ExtractRowVersion(before), AppClient.ExtractRowVersion(after));
        Assert.DoesNotContain("company B", after);
    }

    [Fact]
    public async Task Tickets_can_be_given_only_to_staff_of_the_own_workspace()
    {
        var (_, other) = await TwoCompaniesAsync();
        var home = await SignedInAsUserAsync("Operator", "olga@company-a.test");
        var strangerId = await CreateUserAsync("Operator", "oleg@company-b.test", workspaceId: other);

        var page = await home.GetHtmlAsync("/Tickets/Details/1");
        Assert.Contains($"value=\"{await UserIdAsync("olga@company-a.test")}\"", page);
        Assert.DoesNotContain(strangerId, page);

        var response = await home.PostTicketActionAsync(1, "Assign", new Dictionary<string, string> { ["assigneeId"] = strangerId });
        var after = await home.GetHtmlAsync(response.Headers.Location!.OriginalString);

        Assert.Contains("operators and admins of this help desk only", after);
        Assert.DoesNotContain("oleg@company-b.test", after);
    }

    [Fact]
    public async Task The_database_reads_tickets_of_the_current_workspace_only()
    {
        var (_, other) = await TwoCompaniesAsync();
        var options = new DbContextOptionsBuilder<HelpdeskDbContext>().UseSqlServer(ConnectionString).Options;

        async Task<List<string>> SubjectsSeenAsync(HttpContext? request, bool everyWorkspace = false)
        {
            await using var db = new HelpdeskDbContext(options, new CurrentWorkspace(new HttpContextAccessor { HttpContext = request }));
            var tickets = everyWorkspace ? db.Tickets.IgnoreQueryFilters() : db.Tickets;

            // No workspace condition here on purpose: the filter of the context alone must keep the others out.
            return await tickets.OrderBy(t => t.Id).Select(t => t.Subject).ToListAsync();
        }

        static HttpContext SignedInTo(int workspaceId) => new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(AppClaimTypes.Workspace, workspaceId.ToString(CultureInfo.InvariantCulture))], "test")),
        };

        Assert.Equal(["Only matter of company B"], await SubjectsSeenAsync(SignedInTo(other)));
        Assert.Equal(["First matter of company A", "Second matter of company A"], await SubjectsSeenAsync(SignedInTo(SharedWorkspace.Id)));
        Assert.Empty(await SubjectsSeenAsync(new DefaultHttpContext()));
        Assert.Empty(await SubjectsSeenAsync(null));
        Assert.Equal(3, (await SubjectsSeenAsync(null, everyWorkspace: true)).Count);
    }

    [Fact]
    public async Task Staff_cannot_attach_a_file_to_a_ticket_of_another_workspace()
    {
        var (customer, other) = await TwoCompaniesAsync();
        var stranger = await SignedInAsUserAsync("Operator", "oleg@company-b.test", other);

        // The own #2 does not exist, so the form is posted with a version made up for it.
        var response = await stranger.UploadAttachmentAsync(1, "intruder.png", Png, FakeVersion, postTo: 2);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("intruder.png", await customer.GetHtmlAsync("/Tickets/Details/2"));
    }
}

using Helpdesk.Core.Notifications;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

/// <summary>What the people of a workspace are told about its tickets, as it is stored for them.</summary>
public class NotificationTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private static Dictionary<string, string> Fields(params (string Key, string Value)[] pairs)
        => pairs.ToDictionary(p => p.Key, p => p.Value);

    /// <summary>What each person was told, as (recipient e-mail, kind, detail), oldest first.</summary>
    private async Task<List<(string To, NotificationKind Kind, string? Detail)>> ToldAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();

        var rows = await (
            from n in db.Notifications.IgnoreQueryFilters()
            join u in db.Users on n.RecipientId equals u.Id
            orderby n.Id
            select new { u.Email, n.Kind, n.Detail })
            .ToListAsync();

        return rows.Select(r => (To: r.Email!, r.Kind, Detail: (string?)r.Detail)).ToList();
    }

    private async Task ForgetAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>().Notifications.IgnoreQueryFilters().ExecuteDeleteAsync();
    }

    [Fact]
    public async Task A_new_ticket_is_told_to_the_active_staff_of_its_workspace_only()
    {
        await StandardUserIdAsync("Operator");
        await StandardUserIdAsync("Admin");
        await CreateUserAsync("Operator", "away@shared.test", isActive: false);
        var other = await CreateWorkspaceAsync("Company B");
        await CreateUserAsync("Operator", "stranger@company-b.test", workspaceId: other);
        var customer = await SignedInAsync("Customer");

        await customer.CreateTicketAsync("Printer is down");

        Assert.Equal(
            [("admin@shared.test", NotificationKind.NewTicket, null), ("operator@shared.test", NotificationKind.NewTicket, null)],
            (await ToldAsync()).OrderBy(t => t.To, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task Replies_reach_the_other_side_and_internal_notes_stay_with_staff()
    {
        var customer = await SignedInAsync("Customer");
        var number = await customer.CreateTicketAsync("Printer is down");
        var op = await SignedInAsync("Operator");
        await op.PostTicketActionAsync(number, "Assign", Fields(("assigneeId", await StandardUserIdAsync("Operator"))));
        var admin = await SignedInAsync("Admin");
        await ForgetAsync();

        await op.PostTicketActionAsync(number, "Comment", Fields(("body", "Try another cable.")));
        await customer.PostTicketActionAsync(number, "Comment", Fields(("body", "Did not help.")));
        await admin.PostTicketActionAsync(number, "Comment", Fields(("body", "Known issue."), ("isInternal", "true")));

        Assert.Equal(
            [
                ("customer@shared.test", NotificationKind.Comment, null),
                ("operator@shared.test", NotificationKind.Comment, null),
                ("operator@shared.test", NotificationKind.InternalNote, null),
            ],
            await ToldAsync());
    }

    [Fact]
    public async Task Status_changes_and_assignments_are_told_but_never_to_the_one_who_made_them()
    {
        var customer = await SignedInAsync("Customer");
        var number = await customer.CreateTicketAsync("Printer is down");
        var admin = await SignedInAsync("Admin");
        await ForgetAsync();

        await admin.PostTicketActionAsync(number, "Assign", Fields(("assigneeId", await StandardUserIdAsync("Operator"))));
        await admin.PostTicketActionAsync(number, "Status", Fields(("status", "InProgress")));
        await customer.PostTicketActionAsync(number, "Status", Fields(("status", "Closed"), ("reason", "Works now.")));

        Assert.Equal(
            [
                ("operator@shared.test", NotificationKind.Assigned, null),
                ("customer@shared.test", NotificationKind.StatusChanged, "InProgress"),
                ("operator@shared.test", NotificationKind.StatusChanged, "InProgress"),
                ("operator@shared.test", NotificationKind.StatusChanged, "Closed"),
            ],
            await ToldAsync());
    }

    [Fact]
    public async Task A_refused_change_tells_nobody()
    {
        var customer = await SignedInAsync("Customer");
        var number = await customer.CreateTicketAsync("Printer is down");
        var op = await SignedInAsync("Operator");
        await ForgetAsync();

        await customer.PostTicketActionAsync(number, "Status", Fields(("status", "Closed")));

        // A page seen before the customer wrote: its change comes too late.
        var stale = AppClient.ExtractRowVersion(await op.GetHtmlAsync($"/Tickets/Details/{number}"));
        await customer.PostTicketActionAsync(number, "Comment", Fields(("body", "Any news?")));
        await op.PostTicketActionAsync(number, "Status", Fields(("status", "InProgress")), stale);

        Assert.Empty(await ToldAsync());
    }

    [Fact]
    public async Task A_deleted_account_takes_its_notifications_along_and_a_deleted_workspace_all_of_them()
    {
        var acme = await CreateWorkspaceAsync("Acme");
        await CreateUserAsync("Operator", "olga@acme.test", workspaceId: acme);
        var customer = await SignedInAsUserAsync("Customer", "carl@acme.test", acme);
        await customer.CreateTicketAsync("Printer is down");
        Assert.Single(await ToldAsync());

        var admin = await SignedInAsUserAsync("Admin", "boss@acme.test", acme);
        await admin.PostFormAsync("/Admin/Users", "/Admin/Users?handler=Delete", Fields(("id", await UserIdAsync("olga@acme.test"))));
        Assert.Empty(await ToldAsync());

        await customer.CreateTicketAsync("Scanner too");
        Assert.Single(await ToldAsync());
        var owner = NewClient();
        await SignUpAsync(owner, "owner@platform.test", "Olivia Owner");
        var deleted = await owner.PostFormAsync("/Platform", $"/Platform/Workspace/{acme}?handler=DeleteWorkspace", Fields(("confirmName", "Acme")));

        Assert.Contains("deleted", await (await owner.FollowAsync(deleted)).Content.ReadAsStringAsync());
        Assert.Empty(await ToldAsync());
    }
}

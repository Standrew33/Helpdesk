using Helpdesk.Core.Notifications;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Notifications;
using Microsoft.AspNetCore.SignalR.Client;
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

    private static string Count(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, "class=\"bell-count\" data-unread-count( hidden=\"hidden\")?>([^<]*)<");
        Assert.True(match.Success, "The page has no bell.");
        return match.Groups[1].Success ? "none" : match.Groups[2].Value;
    }

    [Fact]
    public async Task The_bell_counts_what_is_unread_and_the_list_says_what_happened()
    {
        var customer = await SignedInAsync("Customer");
        var first = await customer.CreateTicketAsync("Printer is down");
        var second = await customer.CreateTicketAsync("Scanner too");
        var op = await SignedInAsync("Operator");
        await op.PostTicketActionAsync(first, "Comment", Fields(("body", "Try another cable.")));
        await op.PostTicketActionAsync(second, "Status", Fields(("status", "InProgress")));

        Assert.Equal("2", Count(await customer.GetHtmlAsync("/Tickets")));
        var list = await customer.GetHtmlAsync("/Notifications");
        Assert.Contains("<strong>Test operator</strong> moved", list);
        Assert.Contains("#2 Scanner too</span>", list);
        Assert.Contains("to In progress", list);
        Assert.Contains("<strong>Test operator</strong> replied on", list);
        Assert.Contains("Mark all as read", list);

        // Opening a ticket reads what was told about it, and the page drawn already counts it so.
        Assert.Equal("1", Count(await customer.GetHtmlAsync($"/Tickets/Details/{first}")));

        await customer.PostFormAsync("/Notifications", "/Notifications?handler=ReadAll", new Dictionary<string, string>());
        Assert.Equal("none", Count(await customer.GetHtmlAsync("/Tickets")));
        Assert.DoesNotContain("Mark all as read", await customer.GetHtmlAsync("/Notifications"));
    }

    [Fact]
    public async Task Everyone_sees_only_their_own_notifications_and_the_owner_has_no_bell()
    {
        await StandardUserIdAsync("Operator");
        var customer = await SignedInAsync("Customer");
        await customer.CreateTicketAsync("Printer is down");
        var op = await SignedInAsync("Operator");

        Assert.Contains("opened", await op.GetHtmlAsync("/Notifications"));
        Assert.Contains("Nothing yet", await customer.GetHtmlAsync("/Notifications"));
        Assert.Equal("none", Count(await customer.GetHtmlAsync("/Tickets")));

        var owner = NewClient();
        await SignUpAsync(owner, "owner@platform.test", "Olivia Owner");
        Assert.DoesNotContain("data-bell", await owner.GetHtmlAsync("/Platform"));
        Assert.NotEqual(System.Net.HttpStatusCode.OK, (await owner.GetAsync("/Notifications")).StatusCode);
    }

    /// <summary>An open page of a user: it counts the times the server said something new is there.</summary>
    private sealed class OpenPage(HubConnection connection) : IAsyncDisposable
    {
        private readonly TaskCompletionSource _heard = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static async Task<OpenPage> OfAsync(AppClient client)
        {
            var connection = await client.ConnectAsync(NotificationHub.Path);
            var page = new OpenPage(connection);
            connection.On(NotificationHub.Notified, () => page._heard.TrySetResult());
            return page;
        }

        public async Task<bool> HeardWithinAsync(TimeSpan time)
            => await Task.WhenAny(_heard.Task, Task.Delay(time)) == _heard.Task;

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    [Fact]
    public async Task An_open_page_of_the_recipient_hears_of_a_new_notification_at_once_and_the_others_do_not()
    {
        var customer = await SignedInAsync("Customer");
        var number = await customer.CreateTicketAsync("Printer is down");
        var op = await SignedInAsync("Operator");
        var admin = await SignedInAsync("Admin");
        await using var customerPage = await OpenPage.OfAsync(customer);
        await using var operatorPage = await OpenPage.OfAsync(op);
        await using var adminPage = await OpenPage.OfAsync(admin);

        await op.PostTicketActionAsync(number, "Comment", Fields(("body", "Try another cable.")));

        Assert.True(await customerPage.HeardWithinAsync(TimeSpan.FromSeconds(10)));
        Assert.False(await operatorPage.HeardWithinAsync(TimeSpan.FromSeconds(1)));
        Assert.False(await adminPage.HeardWithinAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task Only_members_of_a_workspace_open_the_line()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => NewClient().ConnectAsync(NotificationHub.Path));

        var owner = NewClient();
        await SignUpAsync(owner, "owner@platform.test", "Olivia Owner");
        await Assert.ThrowsAnyAsync<Exception>(() => owner.ConnectAsync(NotificationHub.Path));
    }
    [Fact]
    public async Task A_person_is_online_while_a_page_is_open_and_seen_when_the_last_one_closes()
    {
        var id = await CreateUserAsync("Customer", "present@example.test");
        var customer = NewClient();
        await customer.SignInAsync("present@example.test", TestPassword);
        var presence = Factory.Services.GetRequiredService<Presence>();
        var admin = await SignedInAsync("Admin");

        var first = await OpenPage.OfAsync(customer);
        var second = await OpenPage.OfAsync(customer);
        Assert.True(presence.IsOnline(id));
        Assert.Contains("presence presence-online", await admin.GetHtmlAsync("/Admin/Users?q=present"));

        await first.DisposeAsync();
        await Task.Delay(300);
        Assert.True(presence.IsOnline(id));

        await second.DisposeAsync();

        for (var i = 0; i < 50 && presence.IsOnline(id); i++)
        {
            await Task.Delay(100);
        }

        Assert.False(presence.IsOnline(id));
        var users = await admin.GetHtmlAsync("/Admin/Users?q=present");
        Assert.DoesNotContain("presence presence-online", users);
        Assert.Contains("Seen ", users);
    }
}

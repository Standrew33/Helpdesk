using System.Net;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Workspaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

/// <summary>Staff bring people into their help desk by e-mail; the link makes the account in that help desk.</summary>
public class InvitationTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private const string Boss = "boss@acme.test";

    /// <summary>Someone who signed up by themselves and runs the "Acme support" help desk.</summary>
    private async Task<AppClient> AcmeAdminAsync()
    {
        var client = NewClient();
        await SignUpAsync(client, Boss, "Bea Boss", workspace: "Acme support");
        return client;
    }

    private static Task<HttpResponseMessage> InviteAsync(AppClient staff, string email, string role = "Customer")
        => staff.PostFormAsync("/Invitations", "/Invitations", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Role"] = role,
        });

    private string LinkFor(string email) => CapturingEmailSender.InvitationLinkIn(Factory.Mail.LastTo(email));

    /// <summary>Opens the link, accepts it, and finishes the welcome step like the invited person would.</summary>
    private static async Task<HttpResponseMessage> AcceptAsync(AppClient person, string link, string name = "Olga Ops")
    {
        var accepted = await person.PostFormAsync(link, link, new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Equal("/Account/Welcome", accepted.Headers.Location!.OriginalString);
        return await person.PostFormAsync("/Account/Welcome", "/Account/Welcome", new Dictionary<string, string> { ["Input.DisplayName"] = name });
    }

    private async Task<ApplicationUser?> UserAsync(string email)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
    }

    private async Task<IList<string>> RolesOfAsync(string email)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GetRolesAsync((await users.FindByEmailAsync(email))!);
    }

    private async Task ChangeInvitationsAsync(Func<HelpdeskDbContext, Task> change)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await change(scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>());
    }

    [Fact]
    public async Task An_invited_operator_joins_the_help_desk_with_the_link()
    {
        var boss = await AcmeAdminAsync();

        var sent = await InviteAsync(boss, "Olga@Acme.test", "Operator");
        Assert.Contains("Invitation sent to olga@acme.test", await (await boss.FollowAsync(sent)).Content.ReadAsStringAsync());

        var mail = Factory.Mail.LastTo("olga@acme.test");
        Assert.Contains("Acme support", mail.Subject);
        Assert.Contains("as an operator", mail.Text);

        var olga = NewClient();
        Assert.Contains("Join Acme support", await olga.GetHtmlAsync(LinkFor("olga@acme.test")));
        var joined = await AcceptAsync(olga, LinkFor("olga@acme.test"));

        Assert.Equal(HttpStatusCode.Redirect, joined.StatusCode);
        Assert.Equal((await UserAsync(Boss))!.WorkspaceId, (await UserAsync("olga@acme.test"))!.WorkspaceId);
        Assert.Equal(["Operator"], await RolesOfAsync("olga@acme.test"));
        Assert.Contains("Olga Ops", await boss.GetHtmlAsync("/Admin/Users"));
        Assert.DoesNotContain("olga@acme.test", await boss.GetHtmlAsync("/Invitations"));
    }

    [Fact]
    public async Task An_invited_customer_opens_tickets_in_the_help_desk()
    {
        var boss = await AcmeAdminAsync();
        await InviteAsync(boss, "carl@client.test");

        var carl = NewClient();
        await AcceptAsync(carl, LinkFor("carl@client.test"), "Carl Client");
        await carl.CreateTicketAsync("The invoice page is blank");

        Assert.Contains("The invoice page is blank", await boss.GetHtmlAsync("/Tickets"));
    }

    [Fact]
    public async Task Opening_the_link_accepts_nothing_and_the_link_works_once()
    {
        var boss = await AcmeAdminAsync();
        await InviteAsync(boss, "olga@acme.test", "Operator");
        var link = LinkFor("olga@acme.test");

        // Mail scanners open links on their own.
        await NewClient().GetHtmlAsync(link);
        Assert.Null(await UserAsync("olga@acme.test"));
        Assert.Contains("olga@acme.test", await boss.GetHtmlAsync("/Invitations"));

        await AcceptAsync(NewClient(), link);
        Assert.Contains("no longer works", await NewClient().GetHtmlAsync(link));
    }

    [Fact]
    public async Task A_withdrawn_or_replaced_invitation_no_longer_works()
    {
        var boss = await AcmeAdminAsync();
        await InviteAsync(boss, "olga@acme.test", "Operator");
        var first = LinkFor("olga@acme.test");
        await InviteAsync(boss, "olga@acme.test", "Customer");
        var second = LinkFor("olga@acme.test");

        Assert.Contains("no longer works", await NewClient().GetHtmlAsync(first));
        Assert.Contains("Join Acme support", await NewClient().GetHtmlAsync(second));

        var id = await ReadAsync(db => db.Invitations.Where(i => i.Email == "olga@acme.test" && i.Role == "Customer").Select(i => i.Id).SingleAsync());
        await boss.PostFormAsync("/Invitations", "/Invitations?handler=Withdraw", new Dictionary<string, string> { ["id"] = id.ToString() });

        Assert.Contains("no longer works", await NewClient().GetHtmlAsync(second));
        Assert.DoesNotContain("olga@acme.test", await boss.GetHtmlAsync("/Invitations"));
    }

    [Fact]
    public async Task An_invitation_older_than_its_lifetime_no_longer_works()
    {
        var boss = await AcmeAdminAsync();
        await InviteAsync(boss, "olga@acme.test", "Operator");
        await ChangeInvitationsAsync(db => db.Invitations.ExecuteUpdateAsync(set => set.SetProperty(i => i.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));

        Assert.Contains("no longer works", await NewClient().GetHtmlAsync(LinkFor("olga@acme.test")));
    }

    [Fact]
    public async Task An_invitation_withdrawn_after_it_was_opened_creates_no_account()
    {
        var boss = await AcmeAdminAsync();
        await InviteAsync(boss, "olga@acme.test", "Operator");
        var link = LinkFor("olga@acme.test");
        var olga = NewClient();
        await olga.PostFormAsync(link, link, new Dictionary<string, string>());

        await ChangeInvitationsAsync(db => db.Invitations.ExecuteUpdateAsync(set => set.SetProperty(i => i.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        // The welcome form was open before; the form token of any page works for it.
        var welcome = await olga.PostFormAsync("/Account/Login", "/Account/Welcome", new Dictionary<string, string> { ["Input.DisplayName"] = "Olga" });

        Assert.Contains("no longer works", await welcome.Content.ReadAsStringAsync());
        Assert.Null(await UserAsync("olga@acme.test"));
    }

    [Fact]
    public async Task An_address_that_has_an_account_cannot_join_another_help_desk()
    {
        var boss = await AcmeAdminAsync();
        await CreateUserAsync("Customer", "taken@example.test");
        await InviteAsync(boss, "taken@example.test");
        var link = LinkFor("taken@example.test");
        var taken = NewClient();

        Assert.Contains("already has an account", await taken.GetHtmlAsync(link));
        var response = await taken.PostFormAsync("/Account/Login", link, new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("already has an account", await response.Content.ReadAsStringAsync());
        Assert.Equal(SharedWorkspace.Id, (await UserAsync("taken@example.test"))!.WorkspaceId);
    }

    [Fact]
    public async Task Operators_invite_customers_only()
    {
        var boss = await AcmeAdminAsync();
        await InviteAsync(boss, "olga@acme.test", "Operator");
        var olga = NewClient();
        await AcceptAsync(olga, LinkFor("olga@acme.test"));

        Assert.DoesNotContain("<select", await olga.GetHtmlAsync("/Invitations"));
        var refused = await InviteAsync(olga, "friend@acme.test", "Admin");
        Assert.Contains("You cannot invite people with this role", await refused.Content.ReadAsStringAsync());
        Assert.Empty(Factory.Mail.To("friend@acme.test"));

        Assert.Equal(HttpStatusCode.Redirect, (await InviteAsync(olga, "carl@client.test")).StatusCode);
        Assert.Single(Factory.Mail.To("carl@client.test"));
    }

    [Fact]
    public async Task Customers_cannot_invite_anyone()
    {
        var customer = await SignedInAsUserAsync("Customer", "carl@client.test");

        var page = await customer.GetAsync("/Invitations");

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/Account/AccessDenied", page.Headers.Location!.OriginalString);
        Assert.DoesNotContain("Invitations", await customer.GetHtmlAsync("/Tickets"));
    }

    [Fact]
    public async Task The_demo_staff_cannot_send_invitations()
    {
        var demo = await SignedInAsync("Admin");

        var response = await InviteAsync(demo, "victim@example.test");

        Assert.Contains("The demo accounts cannot send invitations", await response.Content.ReadAsStringAsync());
        Assert.Empty(Factory.Mail.To("victim@example.test"));
    }

    [Fact]
    public async Task A_help_desk_sends_a_limited_number_of_invitations_a_day()
    {
        var boss = await AcmeAdminAsync();
        var workspaceId = (await UserAsync(Boss))!.WorkspaceId!.Value;
        await ChangeInvitationsAsync(async db =>
        {
            db.Invitations.AddRange(Enumerable.Range(0, InvitationService.MaxPerDay).Select(n => new Invitation
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Email = $"person{n}@example.test",
                Role = "Customer",
                TokenHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(6),
            }));
            await db.SaveChangesAsync();
        });

        var response = await InviteAsync(boss, "one.more@example.test");

        Assert.Contains("invitations in the last 24 hours", await response.Content.ReadAsStringAsync());
        Assert.Empty(Factory.Mail.To("one.more@example.test"));
    }

    [Fact]
    public async Task Invitations_of_another_help_desk_are_out_of_reach()
    {
        var boss = await AcmeAdminAsync();
        await InviteAsync(boss, "olga@acme.test", "Operator");
        var id = await ReadAsync(db => db.Invitations.Select(i => i.Id).SingleAsync());

        var stranger = NewClient();
        await SignUpAsync(stranger, "eve@other.test", "Eve", workspace: "Other desk");

        Assert.DoesNotContain("olga@acme.test", await stranger.GetHtmlAsync("/Invitations"));
        var response = await stranger.PostFormAsync("/Invitations", "/Invitations?handler=Withdraw", new Dictionary<string, string> { ["id"] = id.ToString() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("olga@acme.test", await boss.GetHtmlAsync("/Invitations"));
    }

    [Fact]
    public async Task A_failed_e_mail_leaves_no_invitation_behind()
    {
        var boss = await AcmeAdminAsync();
        Factory.Mail.Refusing = true;

        var response = await InviteAsync(boss, "olga@acme.test", "Operator");

        Factory.Mail.Refusing = false;
        Assert.Contains("Could not send the e-mail", await response.Content.ReadAsStringAsync());
        Assert.False(await ReadAsync(db => db.Invitations.AnyAsync()));
    }

    private async Task<T> ReadAsync<T>(Func<HelpdeskDbContext, Task<T>> read)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>());
    }
}

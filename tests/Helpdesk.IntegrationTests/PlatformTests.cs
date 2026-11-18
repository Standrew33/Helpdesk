using System.Net;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

/// <summary>
/// The owner of the platform, named by configuration, looks into every help desk and can block help desks and
/// people. Nobody else gets there, and the owner reads tickets without writing into them.
/// </summary>
public class PlatformTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private const string Owner = "owner@platform.test";
    private const string Boss = "boss@acme.test";

    private async Task<AppClient> OwnerAsync()
    {
        var client = NewClient();
        await SignUpAsync(client, Owner, "Olivia Owner");
        return client;
    }

    /// <summary>The Acme help desk with its admin and a customer who opened ticket #1, with a public reply and an internal note.</summary>
    private async Task<(AppClient Boss, AppClient Customer, int WorkspaceId)> AcmeAsync()
    {
        var boss = NewClient();
        await SignUpAsync(boss, Boss, "Bea Boss", workspace: "Acme support");
        var acme = (await UserAsync(Boss))!.WorkspaceId!.Value;

        var customer = await SignedInAsUserAsync("Customer", "carl@client.test", acme);
        await customer.CreateTicketAsync("The invoice page is blank", "Nothing shows after I log in.");
        await boss.PostTicketActionAsync(1, "Comment", new Dictionary<string, string> { ["body"] = "We are looking into it." });
        await boss.PostTicketActionAsync(1, "Comment", new Dictionary<string, string> { ["body"] = "Caused by the last release.", ["isInternal"] = "true" });

        return (boss, customer, acme);
    }

    private async Task<ApplicationUser?> UserAsync(string email)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
    }

    private static Task<HttpResponseMessage> BlockAsync(AppClient owner, int workspaceId, bool blocked)
        => owner.PostFormAsync("/Platform", "/Platform?handler=Block", new Dictionary<string, string>
        {
            ["id"] = workspaceId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["blocked"] = blocked ? "true" : "false",
        });

    private static Task<HttpResponseMessage> SetActiveAsync(AppClient owner, int workspaceId, string userId, bool active)
        => owner.PostFormAsync("/Platform", $"/Platform/Workspace/{workspaceId}?handler=Active", new Dictionary<string, string>
        {
            ["userId"] = userId,
            ["active"] = active ? "true" : "false",
        });

    [Fact]
    public async Task The_owner_address_signs_up_outside_every_help_desk()
    {
        var client = NewClient();
        await SignInWithCodeAsync(client, Owner);

        var welcome = await client.GetHtmlAsync("/Account/Welcome");
        Assert.Contains("Welcome, superadmin", welcome);
        Assert.DoesNotContain("Name of your help desk", welcome);

        await client.PostFormAsync("/Account/Welcome", "/Account/Welcome", new Dictionary<string, string> { ["Input.DisplayName"] = "Olivia Owner" });

        Assert.Null((await UserAsync(Owner))!.WorkspaceId);
        var landing = await client.GetAsync("/Tickets");
        Assert.Equal("/Platform", landing.Headers.Location!.OriginalString);

        var platform = await client.GetHtmlAsync("/Platform");
        Assert.Contains($"<span class=\"user-email\">{Owner}</span>", platform);
        Assert.Contains("Help desks", platform);
        Assert.DoesNotContain("href=\"/Tickets\"", platform);
    }

    [Fact]
    public async Task Nobody_else_reaches_the_platform()
    {
        var (boss, customer, acme) = await AcmeAsync();
        var sharedAdmin = await SignedInAsync("Admin");

        foreach (var client in new[] { boss, customer, sharedAdmin })
        {
            foreach (var url in new[] { "/Platform", $"/Platform/Workspace/{acme}", $"/Platform/Ticket/{acme}/1" })
            {
                var response = await client.GetAsync(url);
                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                Assert.Contains("/Account/AccessDenied", response.Headers.Location!.OriginalString);
            }
        }

        var block = await boss.PostFormAsync("/Tickets", "/Platform?handler=Block", new Dictionary<string, string>
        {
            ["id"] = acme.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["blocked"] = "true",
        });
        Assert.Contains("/Account/AccessDenied", block.Headers.Location!.OriginalString);
        Assert.Contains("The invoice page is blank", await customer.GetHtmlAsync("/Tickets"));
    }

    [Fact]
    public async Task An_unproven_owner_address_gives_no_power()
    {
        var impostor = await SignedInAsUserAsync("Customer", Owner);

        var response = await impostor.GetAsync("/Platform");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_owner_sees_every_help_desk_its_people_and_its_tickets_with_internal_notes()
    {
        var (_, _, acme) = await AcmeAsync();
        var other = NewClient();
        await SignUpAsync(other, "eve@other.test", "Eve", workspace: "Other desk");
        var owner = await OwnerAsync();

        var list = await owner.GetHtmlAsync("/Platform");
        Assert.Contains("Acme support", list);
        Assert.Contains("Other desk", list);

        var workspace = await owner.GetHtmlAsync($"/Platform/Workspace/{acme}");
        Assert.Contains(Boss, workspace);
        Assert.Contains("carl@client.test", workspace);
        Assert.Contains("The invoice page is blank", workspace);
        Assert.DoesNotContain("eve@other.test", workspace);

        var ticket = await owner.GetHtmlAsync($"/Platform/Ticket/{acme}/1");
        Assert.Contains("Nothing shows after I log in.", ticket);
        Assert.Contains("We are looking into it.", ticket);
        Assert.Contains("Caused by the last release.", ticket);
        Assert.Contains("Internal note", ticket);
    }

    [Fact]
    public async Task The_owner_reads_tickets_but_does_not_write_into_them()
    {
        var (boss, _, acme) = await AcmeAsync();
        var owner = await OwnerAsync();

        Assert.DoesNotContain("handler=", await owner.GetHtmlAsync($"/Platform/Ticket/{acme}/1"));

        var comment = await owner.PostFormAsync("/Platform", "/Tickets/Details/1?handler=Comment", new Dictionary<string, string>
        {
            ["rowVersion"] = "AAAAAAAAB9E=",
            ["body"] = "Written by the owner",
        });

        Assert.Equal(HttpStatusCode.Redirect, comment.StatusCode);
        Assert.Contains("/Account/AccessDenied", comment.Headers.Location!.OriginalString);
        Assert.DoesNotContain("Written by the owner", await boss.GetHtmlAsync("/Tickets/Details/1"));
    }

    [Fact]
    public async Task A_blocked_help_desk_is_signed_out_and_kept_out_until_unblocked()
    {
        var (boss, customer, acme) = await AcmeAsync();
        await CreateUserAsync("Operator", "olga@acme.test", workspaceId: acme);
        var owner = await OwnerAsync();

        await BlockAsync(owner, acme, blocked: true);

        Assert.Contains("Blocked", await owner.GetHtmlAsync("/Platform"));
        foreach (var client in new[] { boss, customer })
        {
            var response = await client.GetAsync("/Tickets");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString);
        }

        var byCode = await SignInWithCodeAsync(NewClient(), "olga@acme.test");
        Assert.Equal(HttpStatusCode.OK, byCode.StatusCode);
        Assert.Contains("deactivated", await byCode.Content.ReadAsStringAsync());

        // Signing in again is refused like a wrong password, which tells nothing about the account.
        var refused = await NewClient().SignInAsync("carl@client.test", TestPassword);
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);

        await BlockAsync(owner, acme, blocked: false);
        var back = NewClient();
        Assert.Equal(HttpStatusCode.Redirect, (await back.SignInAsync("carl@client.test", TestPassword)).StatusCode);
        Assert.Contains("The invoice page is blank", await back.GetHtmlAsync("/Tickets"));
    }

    [Fact]
    public async Task Blocking_one_help_desk_leaves_the_others_working()
    {
        var (_, _, acme) = await AcmeAsync();
        var other = NewClient();
        await SignUpAsync(other, "eve@other.test", "Eve", workspace: "Other desk");
        var owner = await OwnerAsync();

        await BlockAsync(owner, acme, blocked: true);

        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/Tickets")).StatusCode);
    }

    [Fact]
    public async Task The_owner_switches_off_an_administrator_of_a_help_desk()
    {
        var (boss, customer, acme) = await AcmeAsync();
        var owner = await OwnerAsync();
        var bossId = (await UserAsync(Boss))!.Id;

        await SetActiveAsync(owner, acme, bossId, active: false);

        Assert.Contains("Deactivated", await owner.GetHtmlAsync($"/Platform/Workspace/{acme}"));
        Assert.Contains("/Account/Login", (await boss.GetAsync("/Tickets")).Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync("/Tickets")).StatusCode);

        await SetActiveAsync(owner, acme, bossId, active: true);
        Assert.True((await UserAsync(Boss))!.IsActive);
    }

    [Fact]
    public async Task The_owner_cannot_be_switched_off()
    {
        var (_, _, acme) = await AcmeAsync();
        var owner = await OwnerAsync();

        var response = await SetActiveAsync(owner, acme, (await UserAsync(Owner))!.Id, active: false);

        Assert.Contains("cannot be switched off", await (await owner.FollowAsync(response)).Content.ReadAsStringAsync());
        Assert.True((await UserAsync(Owner))!.IsActive);
    }

    [Fact]
    public async Task An_unknown_help_desk_or_ticket_is_not_found()
    {
        var owner = await OwnerAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/Platform/Workspace/9999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/Platform/Ticket/{SharedWorkspace.Id}/9999")).StatusCode);
    }
}

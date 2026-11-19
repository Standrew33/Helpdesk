using System.Net;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Attachments;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

/// <summary>
/// A deleted person is gone for good, while what they wrote stays signed "Deleted user". A deleted help desk takes
/// everything in it along, its stored files included.
/// </summary>
public class DeletionTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private const string Owner = "owner@platform.test";
    private const string Boss = "boss@acme.test";
    private const string Carl = "carl@client.test";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

    /// <summary>Acme with its admin and a customer who opened ticket #1, commented on it and attached a file.</summary>
    private async Task<(AppClient Boss, AppClient Customer, int WorkspaceId)> AcmeAsync()
    {
        var boss = NewClient();
        await SignUpAsync(boss, Boss, "Bea Boss", workspace: "Acme support");
        var acme = (await UserAsync(Boss))!.WorkspaceId!.Value;

        var customer = await SignedInAsUserAsync("Customer", Carl, acme);
        await customer.CreateTicketAsync("The invoice page is blank");
        await customer.PostTicketActionAsync(1, "Comment", new Dictionary<string, string> { ["body"] = "Still blank this morning." });
        Assert.Equal(HttpStatusCode.Redirect, (await customer.UploadAttachmentAsync(1, "screen.png", Png)).StatusCode);

        return (boss, customer, acme);
    }

    private async Task<AppClient> OwnerAsync()
    {
        var client = NewClient();
        await SignUpAsync(client, Owner, "Olivia Owner");
        return client;
    }

    private async Task<ApplicationUser?> UserAsync(string email)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
    }

    private async Task<T> ReadAsync<T>(Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider);
    }

    private static Task<HttpResponseMessage> AdminDeletesAsync(AppClient admin, string userId)
        => admin.PostFormAsync("/Admin/Users", "/Admin/Users?handler=Delete", new Dictionary<string, string> { ["id"] = userId });

    private static Task<HttpResponseMessage> DeleteWorkspaceAsync(AppClient owner, int workspaceId, string confirmName)
        => owner.PostFormAsync("/Platform", $"/Platform/Workspace/{workspaceId}?handler=DeleteWorkspace", new Dictionary<string, string> { ["confirmName"] = confirmName });

    [Fact]
    public async Task A_deleted_person_is_gone_but_what_they_wrote_stays()
    {
        var (boss, customer, _) = await AcmeAsync();
        var carlId = (await UserAsync(Carl))!.Id;

        var deleted = await AdminDeletesAsync(boss, carlId);
        Assert.Contains("Account deleted", await (await boss.FollowAsync(deleted)).Content.ReadAsStringAsync());

        Assert.DoesNotContain(Carl, await boss.GetHtmlAsync("/Admin/Users"));
        var ticket = await boss.GetHtmlAsync("/Tickets/Details/1");
        Assert.Contains("The invoice page is blank", ticket);
        Assert.Contains("Still blank this morning.", ticket);
        Assert.Contains("screen.png", ticket);
        Assert.Contains("Deleted user", ticket);
        Assert.DoesNotContain(Carl, ticket);

        // The session ends at once and the old password opens nothing.
        Assert.Contains("/Account/Login", (await customer.GetAsync("/Tickets")).Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await NewClient().SignInAsync(Carl, TestPassword)).StatusCode);

        var row = await ReadAsync(sp => sp.GetRequiredService<HelpdeskDbContext>().Users.SingleAsync(u => u.Id == carlId));
        Assert.Equal("Deleted user", row.DisplayName);
        Assert.NotNull(row.DeletedAt);
        Assert.Null(row.PasswordHash);
        Assert.DoesNotContain("carl", row.Email!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_address_of_a_deleted_person_can_sign_up_again()
    {
        var (boss, _, _) = await AcmeAsync();
        await AdminDeletesAsync(boss, (await UserAsync(Carl))!.Id);

        var again = NewClient();
        var response = await SignUpAsync(again, Carl, "Carl Again");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("Carl Again", (await UserAsync(Carl))!.DisplayName);
        Assert.DoesNotContain("The invoice page is blank", await again.GetHtmlAsync("/Tickets"));
    }

    [Fact]
    public async Task An_admin_deletes_only_other_people_of_the_own_help_desk()
    {
        var (boss, _, _) = await AcmeAsync();
        var other = NewClient();
        await SignUpAsync(other, "eve@other.test", "Eve", workspace: "Other desk");

        var self = await AdminDeletesAsync(boss, (await UserAsync(Boss))!.Id);
        Assert.Contains("You cannot delete your own account", await (await boss.FollowAsync(self)).Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await AdminDeletesAsync(other, (await UserAsync(Carl))!.Id)).StatusCode);
        Assert.Null((await UserAsync(Carl))!.DeletedAt);
        Assert.Null((await UserAsync(Boss))!.DeletedAt);
    }


    [Fact]
    public async Task The_owner_deletes_an_administrator_of_a_help_desk()
    {
        var (boss, _, acme) = await AcmeAsync();
        var owner = await OwnerAsync();

        await owner.PostFormAsync("/Platform", $"/Platform/Workspace/{acme}?handler=Delete", new Dictionary<string, string>
        {
            ["userId"] = (await UserAsync(Boss))!.Id,
        });

        Assert.Null(await UserAsync(Boss));
        Assert.DoesNotContain(Boss, await owner.GetHtmlAsync($"/Platform/Workspace/{acme}"));
        Assert.Contains("/Account/Login", (await boss.GetAsync("/Tickets")).Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_owner_deletes_a_help_desk_with_everything_in_it()
    {
        var (boss, customer, acme) = await AcmeAsync();
        var other = NewClient();
        await SignUpAsync(other, "eve@other.test", "Eve", workspace: "Other desk");
        var dan = await SignedInAsUserAsync("Customer", "dan@other.test", (await UserAsync("eve@other.test"))!.WorkspaceId!.Value);
        await dan.CreateTicketAsync("Untouched matter");
        var owner = await OwnerAsync();
        var storedName = await ReadAsync(sp => sp.GetRequiredService<HelpdeskDbContext>().Tickets.IgnoreQueryFilters()
            .Where(t => t.WorkspaceId == acme).SelectMany(t => t.Attachments).Select(a => a.StoredName).SingleAsync());

        var wrong = await DeleteWorkspaceAsync(owner, acme, "acme support");
        Assert.Contains("Type the name of the workspace exactly", await (await owner.FollowAsync(wrong)).Content.ReadAsStringAsync());
        Assert.NotNull(await UserAsync(Boss));

        var done = await DeleteWorkspaceAsync(owner, acme, "Acme support");
        Assert.Contains("The workspace is deleted", await (await owner.FollowAsync(done)).Content.ReadAsStringAsync());

        Assert.DoesNotContain("Acme support", await owner.GetHtmlAsync("/Platform"));
        Assert.Null(await UserAsync(Boss));
        Assert.Null(await UserAsync(Carl));
        Assert.Equal(0, await ReadAsync(sp => sp.GetRequiredService<HelpdeskDbContext>().Tickets.IgnoreQueryFilters().CountAsync(t => t.WorkspaceId == acme)));
        Assert.Null(await ReadAsync(sp => sp.GetRequiredService<IAttachmentStore>().OpenReadAsync(storedName)));

        Assert.Contains("/Account/Login", (await boss.GetAsync("/Tickets")).Headers.Location!.OriginalString);
        Assert.Contains("/Account/Login", (await customer.GetAsync("/Tickets")).Headers.Location!.OriginalString);
        Assert.Contains("Untouched matter", await other.GetHtmlAsync("/Tickets"));
    }


    [Fact]
    public async Task Only_the_owner_deletes_help_desks()
    {
        var (boss, _, acme) = await AcmeAsync();

        var response = await boss.PostFormAsync("/Tickets", $"/Platform/Workspace/{acme}?handler=DeleteWorkspace", new Dictionary<string, string>
        {
            ["confirmName"] = "Acme support",
        });

        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.OriginalString);
        Assert.NotNull(await UserAsync(Boss));
    }
}

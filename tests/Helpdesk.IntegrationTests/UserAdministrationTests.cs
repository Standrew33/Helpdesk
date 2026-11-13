using System.Net;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

public class UserAdministrationTests(SqlServerFixture sql) : WebTestBase(sql)
{
    /// <summary>A real administrator (not the shared demo one), signed in with the sign-in form.</summary>
    private async Task<AppClient> RealAdminAsync(string email = "boss@example.test")
    {
        await CreateUserAsync("Admin", email);
        var client = NewClient();
        await client.SignInAsync(email, TestPassword);
        return client;
    }

    private static Task<HttpResponseMessage> ChangeRoleAsync(AppClient admin, string userId, string role)
        => admin.PostFormAsync("/Tickets", "/Admin/Users?handler=Role", new Dictionary<string, string> { ["id"] = userId, ["role"] = role });

    private static Task<HttpResponseMessage> SetActiveAsync(AppClient admin, string userId, bool active)
        => admin.PostFormAsync("/Tickets", "/Admin/Users?handler=Active", new Dictionary<string, string> { ["id"] = userId, ["active"] = active ? "true" : "false" });

    private static async Task<string> AfterAsync(AppClient client, HttpResponseMessage redirect)
        => await (await client.FollowAsync(redirect)).Content.ReadAsStringAsync();

    [Fact]
    public async Task Anonymous_visitors_are_sent_to_the_sign_in_page()
    {
        var response = await NewClient().GetAsync("/Admin/Users");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Operator")]
    public async Task Only_administrators_can_open_the_user_list(string role)
    {
        var client = await SignedInAsync(role);

        var response = await client.GetAsync("/Admin/Users");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Only_administrators_can_change_roles()
    {
        var customerId = await CreateUserAsync("Customer", "victim@example.test");
        var op = await SignedInAsync("Operator");

        var response = await ChangeRoleAsync(op, customerId, "Admin");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_administrator_sees_the_users_with_their_roles()
    {
        await CreateUserAsync("Customer", "zoe@example.test");
        var admin = await RealAdminAsync();

        var html = await admin.GetHtmlAsync("/Admin/Users");

        Assert.Contains("zoe@example.test", html);
        Assert.Contains("admin@helpdesk.test", html);
        Assert.Contains("Demo account", html);
    }

    [Fact]
    public async Task Search_narrows_the_list()
    {
        await CreateUserAsync("Customer", "zoe@example.test");
        await CreateUserAsync("Customer", "adam@example.test");
        var admin = await RealAdminAsync();

        var html = await admin.GetHtmlAsync("/Admin/Users?q=zoe");

        Assert.Contains("zoe@example.test", html);
        Assert.DoesNotContain("adam@example.test", html);
    }

    [Fact]
    public async Task A_customer_can_be_made_an_operator_and_works_the_queue_after_signing_in_again()
    {
        var customerId = await CreateUserAsync("Customer", "zoe@example.test");
        var other = await SignedInAsync("Customer");
        var ticketId = await other.CreateTicketAsync("Somebody else's ticket");
        var admin = await RealAdminAsync();

        var zoe = NewClient();
        await zoe.SignInAsync("zoe@example.test", TestPassword);
        Assert.Equal(HttpStatusCode.NotFound, (await zoe.GetAsync($"/Tickets/Details/{ticketId}")).StatusCode);

        var response = await ChangeRoleAsync(admin, customerId, "Operator");
        Assert.Contains("Role updated", await AfterAsync(admin, response));

        // The old session no longer counts: the person has to sign in again to get the new role.
        Assert.Equal(HttpStatusCode.Redirect, (await zoe.GetAsync("/Tickets")).StatusCode);
        await zoe.SignInAsync("zoe@example.test", TestPassword);
        Assert.Equal(HttpStatusCode.OK, (await zoe.GetAsync($"/Tickets/Details/{ticketId}")).StatusCode);
    }

    [Fact]
    public async Task An_operator_demoted_to_customer_loses_access_to_other_tickets()
    {
        var operatorId = await CreateUserAsync("Operator", "olga@example.test");
        var customer = await SignedInAsync("Customer");
        var ticketId = await customer.CreateTicketAsync("Needs staff");
        var admin = await RealAdminAsync();

        var olga = NewClient();
        await olga.SignInAsync("olga@example.test", TestPassword);
        Assert.Equal(HttpStatusCode.OK, (await olga.GetAsync($"/Tickets/Details/{ticketId}")).StatusCode);

        await ChangeRoleAsync(admin, operatorId, "Customer");
        await olga.SignInAsync("olga@example.test", TestPassword);

        Assert.Equal(HttpStatusCode.NotFound, (await olga.GetAsync($"/Tickets/Details/{ticketId}")).StatusCode);
    }

    [Fact]
    public async Task A_deactivated_user_is_signed_out_at_once_and_cannot_return()
    {
        var userId = await CreateUserAsync("Customer", "zoe@example.test");
        var admin = await RealAdminAsync();
        var zoe = NewClient();
        await zoe.SignInAsync("zoe@example.test", TestPassword);
        Assert.Equal(HttpStatusCode.OK, (await zoe.GetAsync("/Tickets")).StatusCode);

        var response = await SetActiveAsync(admin, userId, active: false);

        Assert.Contains("Account deactivated", await AfterAsync(admin, response));
        Assert.Equal(HttpStatusCode.Redirect, (await zoe.GetAsync("/Tickets")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await zoe.SignInAsync("zoe@example.test", TestPassword)).StatusCode);
    }

    [Fact]
    public async Task A_deactivated_user_can_be_activated_again()
    {
        var userId = await CreateUserAsync("Customer", "zoe@example.test", isActive: false);
        var admin = await RealAdminAsync();

        var response = await SetActiveAsync(admin, userId, active: true);

        Assert.Contains("Account activated", await AfterAsync(admin, response));
        Assert.Equal(HttpStatusCode.Redirect, (await NewClient().SignInAsync("zoe@example.test", TestPassword)).StatusCode);
    }

    [Fact]
    public async Task An_administrator_cannot_change_their_own_role_or_deactivate_themselves()
    {
        await CreateUserAsync("Admin", "boss@example.test");
        var admin = NewClient();
        await admin.SignInAsync("boss@example.test", TestPassword);
        var ownId = await UserIdAsync("boss@example.test");

        var role = await AfterAsync(admin, await ChangeRoleAsync(admin, ownId, "Customer"));
        var active = await AfterAsync(admin, await SetActiveAsync(admin, ownId, active: false));

        Assert.Contains("cannot change your own role", role);
        Assert.Contains("cannot deactivate your own account", active);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Admin/Users")).StatusCode);
    }

    [Fact]
    public async Task The_demo_accounts_cannot_be_changed()
    {
        var operatorId = await DemoUserIdAsync("Operator");
        var admin = await RealAdminAsync();

        var role = await AfterAsync(admin, await ChangeRoleAsync(admin, operatorId, "Customer"));
        var active = await AfterAsync(admin, await SetActiveAsync(admin, operatorId, active: false));

        Assert.Contains("demo accounts cannot be changed", role);
        Assert.Contains("demo accounts cannot be changed", active);
        Assert.Equal(HttpStatusCode.OK, (await (await SignedInAsync("Operator")).GetAsync("/Tickets")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_role_is_refused()
    {
        var userId = await CreateUserAsync("Customer", "zoe@example.test");
        var admin = await RealAdminAsync();

        var html = await AfterAsync(admin, await ChangeRoleAsync(admin, userId, "Emperor"));

        Assert.Contains("Unknown role", html);
    }

    [Fact]
    public async Task An_unknown_user_is_not_found()
    {
        var admin = await RealAdminAsync();

        var response = await ChangeRoleAsync(admin, "no-such-user", "Customer");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<string> UserIdAsync(string email)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.FindByEmailAsync(email))!.Id;
    }
}

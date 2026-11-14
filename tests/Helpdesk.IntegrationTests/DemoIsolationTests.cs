using System.Net;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

/// <summary>Anyone can sign in as a demo user with one click, so those accounts must never reach real visitors' data.</summary>
public class DemoIsolationTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private async Task<int> RealTicketAsync(string subject, string email = "real.customer@example.test")
    {
        await CreateUserAsync("Customer", email);
        var customer = NewClient();
        await customer.SignInAsync(email, TestPassword);
        return await customer.CreateTicketAsync(subject);
    }

    private async Task<IList<string>> RolesOfAsync(string userId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GetRolesAsync((await users.FindByIdAsync(userId))!);
    }

    [Fact]
    public async Task Demo_staff_do_not_see_tickets_of_real_visitors()
    {
        var realId = await RealTicketAsync("Private matter of a real visitor");

        foreach (var role in new[] { "Operator", "Admin" })
        {
            var demo = await SignedInAsync(role);

            Assert.DoesNotContain("Private matter", await demo.GetHtmlAsync("/Tickets"));
            Assert.Equal(HttpStatusCode.NotFound, (await demo.GetAsync($"/Tickets/Details/{realId}")).StatusCode);
        }
    }

    [Fact]
    public async Task Demo_staff_cannot_change_tickets_of_real_visitors()
    {
        var realId = await RealTicketAsync("Private matter of a real visitor");
        var demo = await SignedInAsync("Operator");

        var response = await demo.PostFormAsync("/Tickets", $"/Tickets/Details/{realId}?handler=Comment", new Dictionary<string, string>
        {
            ["rowVersion"] = "AAAAAAAAB9E=",
            ["body"] = "I should not be able to write here.",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Real_staff_see_every_ticket()
    {
        var realId = await RealTicketAsync("Private matter of a real visitor");
        await CreateUserAsync("Operator", "agent@example.test");
        var op = NewClient();
        await op.SignInAsync("agent@example.test", TestPassword);

        Assert.Contains("Private matter", await op.GetHtmlAsync("/Tickets"));
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync($"/Tickets/Details/{realId}")).StatusCode);
    }

    [Fact]
    public async Task The_demo_administrator_sees_demo_accounts_only()
    {
        await CreateUserAsync("Customer", "real.person@example.test");
        var demo = await SignedInAsync("Admin");

        var html = await demo.GetHtmlAsync("/Admin/Users");

        Assert.DoesNotContain("real.person@example.test", html);
        Assert.Contains("customer@helpdesk.test", html);
        Assert.DoesNotContain("real.person@example.test", await demo.GetHtmlAsync("/Admin/Users?q=real"));
    }

    [Fact]
    public async Task The_demo_administrator_cannot_hand_out_roles_or_lock_accounts()
    {
        var victimId = await CreateUserAsync("Customer", "victim@example.test");
        var demo = await SignedInAsync("Admin");

        var role = await demo.PostFormAsync("/Tickets", "/Admin/Users?handler=Role", new Dictionary<string, string> { ["id"] = victimId, ["role"] = "Admin" });
        var shown = await (await demo.FollowAsync(role)).Content.ReadAsStringAsync();
        var active = await demo.PostFormAsync("/Tickets", "/Admin/Users?handler=Active", new Dictionary<string, string> { ["id"] = victimId, ["active"] = "false" });

        Assert.Contains("cannot change them", shown);
        Assert.Contains("cannot change them", await (await demo.FollowAsync(active)).Content.ReadAsStringAsync());
        Assert.Equal(["Customer"], await RolesOfAsync(victimId));
        Assert.Equal(HttpStatusCode.Redirect, (await NewClient().SignInAsync("victim@example.test", TestPassword)).StatusCode);
    }
}

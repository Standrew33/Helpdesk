using Helpdesk.Core;
using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;
using Helpdesk.Core.Workspaces;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

public class TicketNumberTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private async Task<int> CreateWorkspaceAsync(string name)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var workspace = Workspace.Create(name, DateTimeOffset.UtcNow);
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        return workspace.Id;
    }

    private async Task<int> OpenTicketAsync(string customerId, string subject = "Printer is down")
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var tickets = scope.ServiceProvider.GetRequiredService<TicketService>();
        return await tickets.CreateAsync(new Actor(customerId, UserRole.Customer), subject, "It prints blank pages.", TicketPriority.Normal);
    }

    private async Task<List<(int WorkspaceId, int Number)>> NumbersAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var rows = await db.Tickets.OrderBy(t => t.Id).Select(t => new { t.WorkspaceId, t.Number }).ToListAsync();
        return rows.Select(r => (r.WorkspaceId, r.Number)).ToList();
    }

    [Fact]
    public async Task Every_workspace_numbers_its_tickets_from_one()
    {
        var other = await CreateWorkspaceAsync("Other company");
        var sharedCustomer = await CreateUserAsync("Customer", "shared@example.test");
        var otherCustomer = await CreateUserAsync("Customer", "other@example.test", workspaceId: other);

        await OpenTicketAsync(sharedCustomer);
        await OpenTicketAsync(otherCustomer);
        await OpenTicketAsync(sharedCustomer);
        await OpenTicketAsync(otherCustomer);

        Assert.Equal(
            [(SharedWorkspace.Id, 1), (other, 1), (SharedWorkspace.Id, 2), (other, 2)],
            await NumbersAsync());
    }

    [Fact]
    public async Task Tickets_opened_at_the_same_time_get_different_numbers()
    {
        var customer = await CreateUserAsync("Customer", "busy@example.test");

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => OpenTicketAsync(customer)));

        Assert.Equal(Enumerable.Range(1, 10), (await NumbersAsync()).Select(n => n.Number).Order());
    }

    [Fact]
    public async Task A_refused_ticket_does_not_use_up_a_number()
    {
        var customer = await CreateUserAsync("Customer", "careful@example.test");

        await Assert.ThrowsAsync<DomainException>(() => OpenTicketAsync(customer, subject: ""));
        await OpenTicketAsync(customer);

        Assert.Equal([(SharedWorkspace.Id, 1)], await NumbersAsync());
    }
}

using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Data;

public static class DatabaseSeeder
{
    private const string DemoDomain = "@helpdesk.test";

    public static string DemoEmail(string role) => $"{role.ToLowerInvariant()}{DemoDomain}";

    /// <summary>True for the addresses reserved for the demo users.</summary>
    public static bool IsDemoAddress(string? email)
        => email is not null && email.EndsWith(DemoDomain, StringComparison.OrdinalIgnoreCase);

    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                Check(await roleManager.CreateAsync(new IdentityRole(role)));
            }
        }

        if (!services.GetRequiredService<IOptions<DemoOptions>>().Value.SeedUsers)
        {
            return;
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in RoleNames.All)
        {
            var email = DemoEmail(role);

            if (await userManager.FindByEmailAsync(email) is not null)
            {
                continue;
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = $"Demo {role.ToLowerInvariant()}",
            };

            // Demo users sign in with the demo buttons only; their password is random and never shown.
            Check(await userManager.CreateAsync(user, $"Aa1!{Guid.NewGuid():N}"));
            Check(await userManager.AddToRoleAsync(user, role));
        }

        if (services.GetRequiredService<IOptions<DemoOptions>>().Value.SeedTickets)
        {
            await SeedTicketsAsync(services, userManager);
        }
    }

    private static async Task SeedTicketsAsync(IServiceProvider services, UserManager<ApplicationUser> userManager)
    {
        var db = services.GetRequiredService<HelpdeskDbContext>();

        if (await db.Tickets.AnyAsync())
        {
            return;
        }

        var customerUser = await userManager.FindByEmailAsync(DemoEmail(RoleNames.Customer));
        var operatorUser = await userManager.FindByEmailAsync(DemoEmail(RoleNames.Operator));

        if (customerUser is null || operatorUser is null)
        {
            return;
        }

        var customer = new Actor(customerUser.Id, UserRole.Customer);
        var op = new Actor(operatorUser.Id, UserRole.Operator);
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();

        // Each ticket is created in the past and moved through the workflow so the list shows every state.
        var vpn = Ticket.Create(
            "VPN disconnects every ten minutes",
            "Since Monday the VPN client drops the connection roughly every ten minutes while I work from home.\nIt happens on Wi-Fi and on a cable.",
            TicketPriority.Normal, customer, now.AddHours(-30));
        vpn.Assign(op, operatorUser.Id, now.AddHours(-29));
        vpn.ChangeStatus(op, TicketStatus.InProgress, now.AddHours(-29));
        vpn.AddComment(op, "Which VPN client version do you use? Please send a screenshot of the About page.", false, now.AddHours(-28));
        vpn.ChangeStatus(op, TicketStatus.WaitingForCustomer, now.AddHours(-28));

        var share = Ticket.Create(
            "No access to the Finance share",
            "After the file server migration I get \"access denied\" on the Finance share.",
            TicketPriority.High, customer, now.AddHours(-5));
        share.Assign(op, operatorUser.Id, now.AddHours(-4));
        share.ChangeStatus(op, TicketStatus.InProgress, now.AddHours(-4));
        share.AddComment(op, "Looking into the permissions on the share now.", false, now.AddHours(-4));
        share.AddComment(op, "The migration script skipped the Finance group, needs a manual fix.", true, now.AddHours(-3));

        var monitor = Ticket.Create(
            "Second monitor for room 3.12",
            "Please add a second monitor to my desk.",
            TicketPriority.Low, customer, now.AddHours(-2));

        var printer = Ticket.Create(
            "Printer on the second floor is offline",
            "The printer next to the kitchen shows an offline status for everyone on the floor.",
            TicketPriority.High, customer, now.AddDays(-3));
        printer.Assign(op, operatorUser.Id, now.AddDays(-3).AddMinutes(20));
        printer.ChangeStatus(op, TicketStatus.InProgress, now.AddDays(-3).AddMinutes(20));
        printer.AddComment(op, "The print server had stopped; I restarted the service and the printer is back online.", false, now.AddDays(-3).AddHours(1));
        printer.ChangeStatus(op, TicketStatus.Closed, now.AddDays(-3).AddHours(1));

        db.Tickets.AddRange(vpn, share, monitor, printer);
        await db.SaveChangesAsync();
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Seeding failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}

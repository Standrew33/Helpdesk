using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Data;

public static class DatabaseSeeder
{
    public static string DemoEmail(string role) => $"{role.ToLowerInvariant()}@helpdesk.test";

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

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Helpdesk.IntegrationTests.Infrastructure;

public sealed class HelpdeskWebFactory(string connectionString, bool demoEnabled = true) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connectionString,
            ["Demo:Enabled"] = demoEnabled.ToString(),
            ["Demo:SeedUsers"] = "true",
        }));
    }
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Helpdesk.IntegrationTests.Infrastructure;

public sealed class HelpdeskWebFactory(string connectionString, bool demoEnabled = true) : WebApplicationFactory<Program>
{
    /// <summary>Attachments of this application instance are kept in a private temporary folder.</summary>
    private readonly string _attachmentsPath = Path.Combine(Path.GetTempPath(), "helpdesk-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connectionString,
            ["Demo:Enabled"] = demoEnabled.ToString(),
            ["Demo:SeedUsers"] = "true",
            ["BlobStorage:LocalPath"] = _attachmentsPath,
            // Hashing is slow on purpose in production; tests create many users. Limits are lifted unless a test sets its own.
            ["Security:PasswordHashIterations"] = "1000",
            ["RequestLimits:SignInAttemptsPerMinute"] = "1000",
            ["RequestLimits:RegistrationsPerHour"] = "1000",
            ["RequestLimits:TicketChangesPer10Minutes"] = "1000",
            ["RequestLimits:UploadsPerHour"] = "1000",
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(_attachmentsPath))
        {
            Directory.Delete(_attachmentsPath, recursive: true);
        }
    }
}

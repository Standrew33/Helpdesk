using Helpdesk.Web.Features.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests.Infrastructure;

public sealed class HelpdeskWebFactory(string connectionString, bool demoEnabled = true) : WebApplicationFactory<Program>
{
    /// <summary>Attachments of this application instance are kept in a private temporary folder.</summary>
    private readonly string _attachmentsPath = Path.Combine(Path.GetTempPath(), "helpdesk-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Every e-mail this application instance sends.</summary>
    public CapturingEmailSender Mail { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connectionString,
            ["Demo:Enabled"] = demoEnabled.ToString(),
            ["Demo:SeedUsers"] = "true",
            ["Platform:OwnerEmail"] = "owner@platform.test",
            ["BlobStorage:LocalPath"] = _attachmentsPath,

            // Hashing is slow on purpose in production; tests create many users.
            ["Security:PasswordHashIterations"] = "1000",

            // Tests send many requests from one address; the limits are lifted unless a test sets its own.
            ["RequestLimits:SignInAttemptsPerMinute"] = "1000",
            ["RequestLimits:CodesPer10Minutes"] = "1000",
            ["RequestLimits:RegistrationsPer10Minutes"] = "1000",
            ["RequestLimits:TicketChangesPer10Minutes"] = "1000",
            ["RequestLimits:UploadsPer10Minutes"] = "1000",
            ["RequestLimits:InvitationsPer10Minutes"] = "1000",
        }));

        builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(Mail));
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

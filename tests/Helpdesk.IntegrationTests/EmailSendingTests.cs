using Helpdesk.Web.Features.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Helpdesk.IntegrationTests;

public sealed class EmailSendingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "helpdesk-mail-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static IEmailSender Resolve(string environment, Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var host = new HostingEnvironment { EnvironmentName = environment, ContentRootPath = Path.GetTempPath() };
        var services = new ServiceCollection().AddLogging().AddEmail(configuration, host);
        return services.BuildServiceProvider().GetRequiredService<IEmailSender>();
    }

    [Fact]
    public async Task Without_a_server_messages_are_saved_as_files_with_both_versions()
    {
        var options = Options.Create(new EmailOptions { From = "helpdesk@example.test", FromName = "Helpdesk" });
        var sender = new PickupFolderEmailSender(_folder, options, NullLogger<PickupFolderEmailSender>.Instance);

        await sender.SendAsync(new EmailMessage("nina@example.test", "Your code", "Code: 123456", "<p>Code: <b>123456</b></p>"));

        var file = Assert.Single(Directory.GetFiles(_folder, "*.eml"));
        var content = await File.ReadAllTextAsync(file);
        Assert.Contains("Subject: Your code", content);
        Assert.Contains("To: nina@example.test", content);
        Assert.Contains("Helpdesk <helpdesk@example.test>", content);
        Assert.Contains("text/plain", content);
        Assert.Contains("text/html", content);
    }

    [Fact]
    public void Development_without_a_server_uses_the_pickup_folder()
        => Assert.IsType<PickupFolderEmailSender>(Resolve(Environments.Development, []));

    [Fact]
    public void A_configured_server_is_used()
        => Assert.IsType<SmtpEmailSender>(Resolve(Environments.Production, new() { ["Email:Host"] = "smtp.example.test" }));

    [Fact]
    public void Production_without_a_server_refuses_to_start()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Resolve(Environments.Production, []));

        Assert.Contains("Email:Host", error.Message);
    }
}

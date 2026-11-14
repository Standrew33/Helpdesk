using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests.Infrastructure;

/// <summary>Gives every test class its own database and web application.</summary>
[Collection(SqlServerTestGroup.Name)]
public abstract class WebTestBase(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly List<AppClient> _clients = [];
    private readonly List<HelpdeskWebFactory> _extraFactories = [];

    protected const string TestPassword = "Quiet-river-bridge-7421";

    protected HelpdeskWebFactory Factory { get; private set; } = null!;

    protected string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        ConnectionString = await sql.CreateDatabaseAsync();
        Factory = new HelpdeskWebFactory(ConnectionString);
    }

    public Task DisposeAsync()
    {
        _clients.ForEach(c => c.Dispose());
        _extraFactories.ForEach(f => f.Dispose());
        Factory.Dispose();
        return Task.CompletedTask;
    }

    protected AppClient NewClient(WebApplicationFactory<Program>? factory = null)
    {
        var client = new AppClient(factory ?? Factory);
        _clients.Add(client);
        return client;
    }

    protected async Task<AppClient> SignedInAsync(string role)
    {
        var client = NewClient();
        await client.SignInAsDemoAsync(role);
        return client;
    }

    /// <summary>A web application on a fresh database with demo sign-in switched off.</summary>
    protected async Task<HelpdeskWebFactory> CreateFactoryWithoutDemoAsync()
    {
        var factory = new HelpdeskWebFactory(await sql.CreateDatabaseAsync(), demoEnabled: false);
        _extraFactories.Add(factory);
        return factory;
    }

    /// <summary>Creates a user with a known password, so tests can use the real sign-in form.</summary>
    protected async Task<string> CreateUserAsync(string role, string email, bool isActive = true)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = email, IsActive = isActive };
        Assert.True((await users.CreateAsync(user, TestPassword)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user.Id;
    }

    protected async Task<string> DemoUserIdAsync(string role)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(DatabaseSeeder.DemoEmail(role));
        return user!.Id;
    }
}

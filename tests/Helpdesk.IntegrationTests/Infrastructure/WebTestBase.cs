using Helpdesk.Core.Workspaces;
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

    /// <summary>
    /// Goes through the e-mail sign-in like a person: asks for a code, reads it from the e-mail and enters it.
    /// Returns the response to the code (a redirect to the tickets, or to the welcome step for a new address).
    /// </summary>
    protected async Task<HttpResponseMessage> SignInWithCodeAsync(AppClient client, string email, string? returnUrl = null)
    {
        var codePage = (await client.RequestCodeAsync(email, returnUrl)).Headers.Location!;
        return await client.EnterCodeAsync(codePage, CapturingEmailSender.CodeIn(Factory.Mail.LastTo(email)));
    }

    /// <summary>Signs up a new address with the e-mail code and the welcome step.</summary>
    protected async Task<HttpResponseMessage> SignUpAsync(
        AppClient client, string email, string name = "Nina Novak", string? password = null, string? workspace = null)
    {
        await SignInWithCodeAsync(client, email);
        var form = new Dictionary<string, string> { ["Input.DisplayName"] = name };

        if (workspace is not null)
        {
            form["Input.WorkspaceName"] = workspace;
        }

        if (password is not null)
        {
            form["Input.Password"] = password;
            form["Input.ConfirmPassword"] = password;
        }

        return await client.PostFormAsync("/Account/Welcome", "/Account/Welcome", form);
    }

    /// <summary>A web application on a fresh database with demo sign-in switched off.</summary>
    protected async Task<HelpdeskWebFactory> CreateFactoryWithoutDemoAsync()
    {
        var factory = new HelpdeskWebFactory(await sql.CreateDatabaseAsync(), demoEnabled: false);
        _extraFactories.Add(factory);
        return factory;
    }

    /// <summary>Creates another help desk next to the shared one.</summary>
    protected async Task<int> CreateWorkspaceAsync(string name)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        var workspace = Workspace.Create(name, DateTimeOffset.UtcNow);
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();
        return workspace.Id;
    }

    /// <summary>Creates a user and signs them in with the password form.</summary>
    protected async Task<AppClient> SignedInAsUserAsync(string role, string email, int workspaceId = SharedWorkspace.Id)
    {
        await CreateUserAsync(role, email, workspaceId: workspaceId);
        var client = NewClient();
        await client.SignInAsync(email, TestPassword);
        return client;
    }

    /// <summary>Creates a user with a known password, so tests can use the real sign-in form.</summary>
    protected async Task<string> CreateUserAsync(string role, string email, bool isActive = true, int workspaceId = SharedWorkspace.Id)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = email,
            IsActive = isActive,
            WorkspaceId = workspaceId,
        };
        Assert.True((await users.CreateAsync(user, TestPassword)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user.Id;
    }

    protected async Task<string> UserIdAsync(string email)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.FindByEmailAsync(email))!.Id;
    }

    protected async Task<string> DemoUserIdAsync(string role)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(DatabaseSeeder.DemoEmail(role));
        return user!.Id;
    }
}

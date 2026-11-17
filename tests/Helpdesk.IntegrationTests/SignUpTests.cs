using System.Net;
using Helpdesk.Core.Workspaces;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

/// <summary>
/// A new address becomes an account only after it was proven with a code. It comes with a help desk of its own,
/// which the new user runs as its administrator.
/// </summary>
public class SignUpTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private const string Email = "nina@example.test";

    private async Task<ApplicationUser?> UserAsync(string email)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
    }

    [Fact]
    public async Task A_new_address_gets_the_welcome_step_and_no_account_yet()
    {
        var client = NewClient();

        var response = await SignInWithCodeAsync(client, Email);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Welcome", response.Headers.Location!.OriginalString);
        Assert.Contains(Email, await client.GetHtmlAsync("/Account/Welcome"));
        Assert.Null(await UserAsync(Email));
    }

    private async Task<Workspace?> WorkspaceOfAsync(string email)
    {
        var user = await UserAsync(email);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>();
        return await db.Workspaces.SingleOrDefaultAsync(w => w.Id == user!.WorkspaceId);
    }

    private async Task<int> WorkspaceCountAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>().Workspaces.CountAsync();
    }

    [Fact]
    public async Task Finishing_the_welcome_step_creates_a_help_desk_run_by_the_new_user()
    {
        var client = NewClient();

        var response = await SignUpAsync(client, Email, "Nina Novak", workspace: "Acme support");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var list = await client.GetHtmlAsync("/Tickets");
        Assert.Contains("Nina Novak", list);
        Assert.Contains("0 tickets in total", list);

        var user = (await UserAsync(Email))!;
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.PasswordHash);
        Assert.NotEqual(SharedWorkspace.Id, user.WorkspaceId);
        Assert.Equal("Acme support", (await WorkspaceOfAsync(Email))!.Name);

        // The new user runs the help desk; nobody else is in it yet.
        var users = await client.GetHtmlAsync("/Admin/Users");
        Assert.Contains(Email, users);
        Assert.DoesNotContain("@helpdesk.test", users);
    }

    [Fact]
    public async Task Without_a_help_desk_name_it_is_called_after_the_user()
    {
        await SignUpAsync(NewClient(), Email, "Nina Novak");

        Assert.Equal("Nina Novak's help desk", (await WorkspaceOfAsync(Email))!.Name);
    }

    [Fact]
    public async Task Everyone_who_signs_up_gets_a_help_desk_of_their_own()
    {
        var nina = NewClient();
        var otto = NewClient();

        await SignUpAsync(nina, Email, "Nina Novak");
        await SignUpAsync(otto, "otto@example.test", "Otto Olsen");

        Assert.NotEqual((await UserAsync(Email))!.WorkspaceId, (await UserAsync("otto@example.test"))!.WorkspaceId);
        Assert.DoesNotContain(Email, await otto.GetHtmlAsync("/Admin/Users"));
        Assert.DoesNotContain("otto@example.test", await nina.GetHtmlAsync("/Admin/Users"));
    }

    [Theory]
    [InlineData("\u202Eevil")]
    [InlineData("A name far too long for a help desk, A name far too long for a help desk, A name far too long for it, really")]
    public async Task A_bad_help_desk_name_is_refused_and_nothing_is_created(string name)
    {
        var workspaces = await WorkspaceCountAsync();

        var response = await SignUpAsync(NewClient(), Email, workspace: System.Text.RegularExpressions.Regex.Unescape(name));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await UserAsync(Email));
        Assert.Equal(workspaces, await WorkspaceCountAsync());
    }

    [Fact]
    public async Task An_optional_password_can_be_set_and_used_later()
    {
        await SignUpAsync(NewClient(), Email, password: "tram7Lamp-ocean");
        var returning = NewClient();

        var response = await returning.SignInAsync(Email, "tram7Lamp-ocean");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await returning.GetAsync("/Tickets")).StatusCode);
    }

    [Theory]
    [InlineData("short1!")]
    [InlineData("password123456")]
    [InlineData("12345678901")]
    public async Task A_weak_password_is_refused_and_no_account_is_created(string password)
    {
        var workspaces = await WorkspaceCountAsync();
        var client = NewClient();

        var response = await SignUpAsync(client, Email, password: password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await UserAsync(Email));
        Assert.Equal(workspaces, await WorkspaceCountAsync());
    }

    [Fact]
    public async Task Without_a_name_the_start_of_the_address_stands_in()
    {
        var response = await SignUpAsync(NewClient(), Email, name: string.Empty);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("nina", (await UserAsync(Email))!.DisplayName);
    }

    [Fact]
    public async Task The_welcome_step_can_be_skipped_whatever_was_typed()
    {
        var client = NewClient();
        await SignInWithCodeAsync(client, Email);

        var response = await client.PostFormAsync("/Account/Welcome", "/Account/Welcome", new Dictionary<string, string>
        {
            ["Input.DisplayName"] = "Nina Novak",
            ["Input.Password"] = "short",
            ["Input.ConfirmPassword"] = "different",
            ["skip"] = "true",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var user = (await UserAsync(Email))!;
        Assert.Equal("nina", user.DisplayName);
        Assert.Null(user.PasswordHash);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Tickets")).StatusCode);
    }

    [Fact]
    public async Task The_welcome_step_needs_a_proven_address()
    {
        var client = NewClient();

        Assert.Contains("/Account/Login", (await client.GetAsync("/Account/Welcome")).Headers.Location!.OriginalString);

        var forged = await client.PostWithoutTokenAsync("/Account/Welcome", new Dictionary<string, string> { ["Input.DisplayName"] = "Mallory" });
        Assert.NotEqual(HttpStatusCode.OK, forged.StatusCode);
    }

    [Fact]
    public async Task An_account_without_a_password_can_set_one_later()
    {
        var client = NewClient();
        await SignUpAsync(client, Email);
        Assert.Contains("Set a password", await client.GetHtmlAsync("/Account/Settings"));

        var response = await client.PostFormAsync("/Account/Settings", "/Account/Settings?handler=SetPassword", new Dictionary<string, string>
        {
            ["NewPassword.Password"] = "tram7Lamp-ocean",
            ["NewPassword.ConfirmPassword"] = "tram7Lamp-ocean",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Change password", await client.GetHtmlAsync("/Account/Settings"));
        Assert.Equal(HttpStatusCode.Redirect, (await NewClient().SignInAsync(Email, "tram7Lamp-ocean")).StatusCode);
    }

    [Fact]
    public async Task An_existing_password_cannot_be_replaced_without_the_current_one()
    {
        await CreateUserAsync("Customer", Email);
        var client = NewClient();
        await client.SignInAsync(Email, TestPassword);

        var response = await client.PostFormAsync("/Account/Settings", "/Account/Settings?handler=SetPassword", new Dictionary<string, string>
        {
            ["NewPassword.Password"] = "tram7Lamp-ocean",
            ["NewPassword.ConfirmPassword"] = "tram7Lamp-ocean",
        });

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await NewClient().SignInAsync(Email, TestPassword)).StatusCode);
    }

    [Fact]
    public async Task The_demo_addresses_cannot_be_used()
    {
        var response = await NewClient().RequestCodeAsync("admin@helpdesk.test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("belongs to the demo", await response.Content.ReadAsStringAsync());
        Assert.Empty(Factory.Mail.Messages);
    }
}

using System.Net;
using Helpdesk.IntegrationTests.Infrastructure;
using Helpdesk.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Helpdesk.IntegrationTests;

/// <summary>A new address becomes an account only after it was proven with a code, and only as a customer.</summary>
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

    [Fact]
    public async Task Finishing_the_welcome_step_creates_a_signed_in_customer()
    {
        var client = NewClient();

        var response = await SignUpAsync(client, Email, "Nina Novak");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var list = await client.GetHtmlAsync("/Tickets");
        Assert.Contains("Nina Novak", list);
        Assert.Contains("New ticket", list);

        var user = (await UserAsync(Email))!;
        Assert.True(user.EmailConfirmed);
        Assert.False(user.PasswordHash is not null);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Admin/Users")).StatusCode);
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
        var client = NewClient();

        var response = await SignUpAsync(client, Email, password: password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await UserAsync(Email));
    }

    [Fact]
    public async Task A_name_is_required()
    {
        var response = await SignUpAsync(NewClient(), Email, name: string.Empty);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await UserAsync(Email));
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

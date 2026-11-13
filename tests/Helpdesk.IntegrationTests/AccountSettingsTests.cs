using System.Net;
using Helpdesk.IntegrationTests.Infrastructure;

namespace Helpdesk.IntegrationTests;

public class AccountSettingsTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private const string NewPassword = "Fresh-secret-9876";

    private async Task<AppClient> SignedInRealUserAsync()
    {
        await CreateUserAsync("Customer", "carol@example.test");
        var client = NewClient();
        await client.SignInAsync("carol@example.test", TestPassword);
        return client;
    }

    private static Task<HttpResponseMessage> PostAsync(AppClient client, string handler, Dictionary<string, string> fields)
        => client.PostFormAsync("/Account/Settings", $"/Account/Settings?handler={handler}", fields);

    [Fact]
    public async Task Settings_page_needs_a_signed_in_user()
    {
        var response = await NewClient().GetAsync("/Account/Settings");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_name_is_changed_and_shown_in_the_menu_straight_away()
    {
        var client = await SignedInRealUserAsync();

        var response = await PostAsync(client, "Profile", new() { ["Profile.DisplayName"] = "Carol Cooper" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Carol Cooper", await client.GetHtmlAsync("/Tickets"));
    }

    [Fact]
    public async Task An_empty_name_is_refused()
    {
        var client = await SignedInRealUserAsync();

        var response = await PostAsync(client, "Profile", new() { ["Profile.DisplayName"] = "" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_password_is_changed_and_the_new_one_works()
    {
        var client = await SignedInRealUserAsync();

        var response = await PostAsync(client, "Password", PasswordForm(TestPassword, NewPassword));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Tickets")).StatusCode);

        var later = NewClient();
        Assert.Equal(HttpStatusCode.OK, (await later.SignInAsync("carol@example.test", TestPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await later.SignInAsync("carol@example.test", NewPassword)).StatusCode);
    }

    [Fact]
    public async Task A_wrong_current_password_changes_nothing()
    {
        var client = await SignedInRealUserAsync();

        var response = await PostAsync(client, "Password", PasswordForm("wrong-current-password", NewPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Incorrect password", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await NewClient().SignInAsync("carol@example.test", TestPassword)).StatusCode);
    }

    [Fact]
    public async Task A_weak_new_password_is_refused()
    {
        var client = await SignedInRealUserAsync();

        var response = await PostAsync(client, "Password", PasswordForm(TestPassword, "weak"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Demo_accounts_cannot_be_edited()
    {
        var demo = await SignedInAsync("Customer");

        var page = await demo.GetHtmlAsync("/Account/Settings");
        var response = await demo.PostFormAsync("/Account/Settings", "/Account/Settings?handler=Profile", new Dictionary<string, string> { ["Profile.DisplayName"] = "Hacked" });

        Assert.Contains("shared demo account", page);
        Assert.DoesNotContain("Change password", page);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain("Hacked", await demo.GetHtmlAsync("/Tickets"));
    }

    private static Dictionary<string, string> PasswordForm(string current, string next) => new()
    {
        ["Password.CurrentPassword"] = current,
        ["Password.NewPassword"] = next,
        ["Password.ConfirmPassword"] = next,
    };
}

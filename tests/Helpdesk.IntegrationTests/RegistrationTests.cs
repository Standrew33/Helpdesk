using System.Net;
using Helpdesk.IntegrationTests.Infrastructure;

namespace Helpdesk.IntegrationTests;

public class RegistrationTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private const string StrongPassword = "Brand-new-pass-42";

    private static Dictionary<string, string> Form(
        string name = "Nina Novak", string email = "nina@example.test", string password = StrongPassword, string? repeat = null)
        => new()
        {
            ["Input.DisplayName"] = name,
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = repeat ?? password,
        };

    private static Task<HttpResponseMessage> RegisterAsync(AppClient client, Dictionary<string, string> form)
        => client.PostFormAsync("/Account/Register", "/Account/Register", form);

    [Fact]
    public async Task Registration_page_is_public()
    {
        Assert.Equal(HttpStatusCode.OK, (await NewClient().GetAsync("/Account/Register")).StatusCode);
    }

    [Fact]
    public async Task Registering_creates_a_customer_who_is_signed_in()
    {
        var client = NewClient();

        var response = await RegisterAsync(client, Form());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var list = await client.GetHtmlAsync("/Tickets");
        Assert.Contains("Nina Novak", list);
        Assert.Contains("New ticket", list);
    }

    [Fact]
    public async Task A_new_account_is_a_customer_and_not_an_administrator()
    {
        var client = NewClient();
        await RegisterAsync(client, Form());

        var response = await client.GetAsync("/Admin/Users");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/AccessDenied", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_new_account_can_sign_in_again_with_its_password()
    {
        await RegisterAsync(NewClient(), Form());
        var returning = NewClient();

        var response = await returning.SignInAsync("nina@example.test", StrongPassword);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await returning.GetAsync("/Tickets")).StatusCode);
    }

    [Fact]
    public async Task An_address_that_is_already_registered_is_refused()
    {
        await CreateUserAsync("Customer", "nina@example.test");

        var response = await RegisterAsync(NewClient(), Form());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("already exists", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("short1A!")]
    [InlineData("alllowercase-1234")]
    [InlineData("NoDigitsHere-abc")]
    public async Task A_weak_password_is_refused(string password)
    {
        var client = NewClient();

        var response = await RegisterAsync(client, Form(password: password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Tickets")).StatusCode);
    }

    [Fact]
    public async Task Different_passwords_are_refused()
    {
        var response = await RegisterAsync(NewClient(), Form(repeat: "Another-pass-42"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("do not match", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_invalid_address_is_refused()
    {
        var response = await RegisterAsync(NewClient(), Form(email: "not-an-address"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_demo_addresses_cannot_be_registered()
    {
        var response = await RegisterAsync(NewClient(), Form(email: "admin@helpdesk.test"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("reserved", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_signed_in_user_is_sent_to_the_tickets_instead_of_the_form()
    {
        var customer = await SignedInAsync("Customer");

        var response = await customer.GetAsync("/Account/Register");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Tickets", response.Headers.Location!.OriginalString);
    }
}

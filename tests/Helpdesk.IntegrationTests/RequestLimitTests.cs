using System.Net;
using Helpdesk.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Helpdesk.IntegrationTests;

public class RequestLimitTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private WebApplicationFactory<Program> WithLimit(string key, int value)
        => Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { [$"RequestLimits:{key}"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture) })));

    [Fact]
    public async Task Too_many_sign_in_attempts_from_one_address_are_stopped()
    {
        using var factory = WithLimit("SignInAttemptsPerMinute", 3);
        var client = NewClient(factory);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.SignInAsync("nobody@example.test", "wrong-password-1234")).StatusCode);
        }

        var blocked = await client.SignInAsync("nobody@example.test", "wrong-password-1234");

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task A_refused_form_is_shown_again_with_the_reason_and_what_was_typed()
    {
        using var factory = WithLimit("CodesPerHour", 1);
        var client = NewClient(factory);
        await client.RequestCodeAsync("first@example.test");

        var blocked = await client.RequestCodeAsync("second@example.test");
        var html = await blocked.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Contains("Too many attempts from your network. Try again in", html);
        Assert.Contains("value=\"second@example.test\"", html);
        Assert.Contains("name=\"Input.Email\"", html);
        Assert.DoesNotContain("Something went wrong", html);
        Assert.Empty(Factory.Mail.To("second@example.test"));
    }

    [Theory]
    [InlineData(1, "1 second")]
    [InlineData(45, "45 seconds")]
    [InlineData(60, "1 minute")]
    [InlineData(61, "1 minute")]
    [InlineData(150, "3 minutes")]
    [InlineData(3600, "60 minutes")]
    public void The_wait_is_told_in_plain_words(int seconds, string wait)
        => Assert.Equal($"Too many attempts from your network. Try again in {wait}.", Helpdesk.Web.Security.RequestLimitFilter.Message(seconds));

    [Fact]
    public async Task A_refused_ticket_change_changes_nothing()
    {
        using var factory = WithLimit("TicketChangesPer10Minutes", 1);
        var customer = NewClient(factory);
        await customer.SignInAsDemoAsync("Customer");
        await customer.CreateTicketAsync("First");

        var blocked = await customer.PostFormAsync("/Tickets/Create", "/Tickets/Create", new Dictionary<string, string>
        {
            ["Input.Subject"] = "Second ticket title",
            ["Input.Description"] = "Should not be saved",
            ["Input.Priority"] = "Normal",
        });

        var html = await blocked.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Contains("Too many attempts from your network", html);
        Assert.Contains("value=\"Second ticket title\"", html);
        Assert.DoesNotContain("Second ticket title", await customer.GetHtmlAsync("/Tickets"));
    }

    [Fact]
    public async Task Reading_pages_is_never_limited()
    {
        using var factory = WithLimit("SignInAttemptsPerMinute", 1);
        var client = NewClient(factory);

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Login")).StatusCode);
        }
    }

    [Fact]
    public async Task Each_address_has_its_own_allowance()
    {
        using var factory = WithLimit("SignInAttemptsPerMinute", 2);
        var first = NewClient(factory);
        first.AddHeader("X-Forwarded-For", "203.0.113.10");
        var second = NewClient(factory);
        second.AddHeader("X-Forwarded-For", "203.0.113.20");

        await first.SignInAsync("nobody@example.test", "wrong-password-1234");
        await first.SignInAsync("nobody@example.test", "wrong-password-1234");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.SignInAsync("nobody@example.test", "wrong-password-1234")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.SignInAsync("nobody@example.test", "wrong-password-1234")).StatusCode);
    }

    [Fact]
    public async Task Registrations_from_one_address_are_limited()
    {
        using var factory = WithLimit("RegistrationsPerHour", 2);

        // Each account is created from a fresh browser, but from the same address.
        for (var i = 0; i < 2; i++)
        {
            var response = await SignUpAsync(NewClient(factory), $"person{i}@example.test");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        var blocked = await SignUpAsync(NewClient(factory), "person9@example.test");

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    [Fact]
    public async Task One_address_cannot_send_codes_to_many_mailboxes()
    {
        using var factory = WithLimit("CodesPerHour", 2);
        var client = NewClient(factory);

        await client.RequestCodeAsync("first@example.test");
        await client.RequestCodeAsync("second@example.test");
        var third = await client.RequestCodeAsync("third@example.test");

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Empty(Factory.Mail.To("third@example.test"));

        // Other sign-in forms have their own allowance.
        await client.SignInAsDemoAsync("Customer");
    }

    [Fact]
    public async Task Uploads_are_limited_per_address()
    {
        using var factory = WithLimit("UploadsPerHour", 1);
        var customer = NewClient(factory);
        await customer.SignInAsDemoAsync("Customer");
        var id = await customer.CreateTicketAsync("Many files");

        await customer.UploadAttachmentAsync(id, "one.txt", "first"u8.ToArray());
        var second = await customer.UploadAttachmentAsync(id, "two.txt", "second"u8.ToArray());

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }
}

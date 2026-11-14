using System.Net;
using System.Text.RegularExpressions;
using Helpdesk.IntegrationTests.Infrastructure;

namespace Helpdesk.IntegrationTests;

public partial class SecurityHeaderTests(SqlServerFixture sql) : WebTestBase(sql)
{
    [Fact]
    public async Task Pages_carry_the_security_headers()
    {
        var response = await NewClient().GetAsync("/Account/Login");

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("camera=()", response.Headers.GetValues("Permissions-Policy").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Pages_contain_no_inline_script_or_style_that_the_policy_would_block()
    {
        await CreateUserAsync("Customer", "page.checker@example.test");
        var customer = NewClient();
        await customer.SignInAsync("page.checker@example.test", TestPassword);
        var ticketId = await customer.CreateTicketAsync("A ticket to look at");
        await CreateUserAsync("Operator", "page.agent@example.test");
        var agent = NewClient();
        await agent.SignInAsync("page.agent@example.test", TestPassword);

        var pages = new List<(AppClient Client, string Url)>
        {
            (NewClient(), "/Account/Login"),
            (NewClient(), "/Account/Register"),
            (customer, "/Tickets"),
            (customer, "/Tickets/Create"),
            (customer, $"/Tickets/Details/{ticketId}"),
            (customer, "/Account/Settings"),
            (agent, $"/Tickets/Details/{ticketId}"),
            (await SignedInAsync("Admin"), "/Admin/Users"),
        };

        foreach (var (client, url) in pages)
        {
            var html = await client.GetHtmlAsync(url);

            Assert.DoesNotMatch(InlineScript(), html);
            Assert.DoesNotMatch(InlineStyleAttribute(), html);
            Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(InlineHandler(), html);
        }
    }

    [Fact]
    public async Task Static_files_keep_their_own_caching()
    {
        var client = NewClient();
        var html = await client.GetHtmlAsync("/Account/Login");
        var stylesheet = Regex.Match(html, "href=\"(/css/site[^\"]*\\.css)\"").Groups[1].Value;

        var response = await client.GetAsync(stylesheet);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Downloads_cannot_run_anything_in_the_browser()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("With a file");
        await customer.UploadAttachmentAsync(id, "notes.txt", "plain text"u8.ToArray());
        var html = await customer.GetHtmlAsync($"/Tickets/Details/{id}");
        var link = Regex.Match(html, "href=\"([^\"]*handler=Download[^\"]*)\"").Groups[1].Value.Replace("&amp;", "&", StringComparison.Ordinal);

        var download = await customer.GetAsync(link);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("default-src 'none'; sandbox", download.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("no-store", download.Headers.CacheControl?.ToString());
    }

    [GeneratedRegex("<script(?![^>]*\\bsrc=)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineScript();

    [GeneratedRegex("<[a-z][^>]*\\sstyle=", RegexOptions.IgnoreCase)]
    private static partial Regex InlineStyleAttribute();

    [GeneratedRegex("<[a-z][^>]*\\son[a-z]+=", RegexOptions.IgnoreCase)]
    private static partial Regex InlineHandler();
}

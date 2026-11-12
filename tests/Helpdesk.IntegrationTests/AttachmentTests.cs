using System.Net;
using System.Text;
using Helpdesk.Core.Tickets;
using Helpdesk.IntegrationTests.Infrastructure;

namespace Helpdesk.IntegrationTests;

public class AttachmentTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private static readonly byte[] Content = Encoding.UTF8.GetBytes("printer log line 1\nprinter log line 2\n");

    private static async Task<string> FollowedHtmlAsync(AppClient client, HttpResponseMessage redirect)
        => await (await client.FollowAsync(redirect)).Content.ReadAsStringAsync();

    private static string DownloadUrl(int ticketId, string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, @"href=""([^""]*handler=Download[^""]*)""");
        Assert.True(match.Success, "The page has no download link.");
        return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [Fact]
    public async Task Customer_attaches_a_file_and_downloads_the_same_content()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Printer logs");

        var response = await customer.UploadAttachmentAsync(id, "printer.log", Content);
        var html = await FollowedHtmlAsync(customer, response);

        Assert.Contains("printer.log", html);
        var download = await customer.GetAsync(DownloadUrl(id, html));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(Content, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("text/plain", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Staff_can_download_a_customers_file_and_the_upload_shows_in_the_history()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("With a file");
        await customer.UploadAttachmentAsync(id, "report.pdf", Content);

        var op = await SignedInAsync("Operator");
        var html = await op.GetHtmlAsync($"/Tickets/Details/{id}");

        Assert.Contains("Attached report.pdf", html);
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync(DownloadUrl(id, html))).StatusCode);
    }

    [Fact]
    public async Task Another_customer_cannot_download_the_file()
    {
        var owner = await SignedInAsync("Customer");
        var id = await owner.CreateTicketAsync("Private file");
        var html = await FollowedHtmlAsync(owner, await owner.UploadAttachmentAsync(id, "secret.txt", Content));
        var url = DownloadUrl(id, html);

        await CreateUserAsync("Customer", "other@example.test");
        var other = NewClient();
        await other.SignInAsync("other@example.test", TestPassword);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_visitor_cannot_download_the_file()
    {
        var owner = await SignedInAsync("Customer");
        var id = await owner.CreateTicketAsync("Private file");
        var html = await FollowedHtmlAsync(owner, await owner.UploadAttachmentAsync(id, "secret.txt", Content));

        var response = await NewClient().GetAsync(DownloadUrl(id, html));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task File_of_a_disallowed_type_is_rejected()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Sneaky file");

        var html = await FollowedHtmlAsync(customer, await customer.UploadAttachmentAsync(id, "tool.exe", Content));

        Assert.Contains("not accepted", html);
        Assert.Contains("No files attached", html);
    }

    [Fact]
    public async Task File_over_the_size_limit_is_rejected()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Huge file");

        var tooBig = new byte[AttachmentPolicy.MaxFileBytes + 1];
        var html = await FollowedHtmlAsync(customer, await customer.UploadAttachmentAsync(id, "big.pdf", tooBig));

        Assert.Contains("larger than", html);
        Assert.Contains("No files attached", html);
    }

    [Fact]
    public async Task Closed_ticket_does_not_accept_files()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Done already");
        await customer.PostTicketActionAsync(id, "Status", new Dictionary<string, string> { ["status"] = "Closed" });

        // Staff still sees a form with the current version on a closed ticket, so the rule itself is what answers.
        var op = await SignedInAsync("Operator");
        var html = await FollowedHtmlAsync(op, await op.UploadAttachmentAsync(id, "late.txt", Content));

        Assert.Contains("is closed", html);
        Assert.DoesNotContain("late.txt", html);
    }

    [Fact]
    public async Task Stale_page_does_not_leave_a_stored_file_behind()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Race");
        var staleVersion = AppClient.ExtractRowVersion(await customer.GetHtmlAsync($"/Tickets/Details/{id}"));
        await customer.PostTicketActionAsync(id, "Comment", new Dictionary<string, string> { ["body"] = "newer change" });

        var html = await FollowedHtmlAsync(customer, await customer.UploadAttachmentAsync(id, "late.txt", Content, staleVersion));

        Assert.Contains("changed by someone else", html);
        Assert.DoesNotContain("late.txt", html);
    }

    [Fact]
    public async Task Upload_without_a_file_shows_a_message()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Nothing attached");

        var html = await FollowedHtmlAsync(customer, await customer.UploadAttachmentAsync(id, "empty.txt", []));

        Assert.Contains("Choose a file", html);
    }
}

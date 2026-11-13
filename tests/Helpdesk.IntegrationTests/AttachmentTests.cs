using System.Net;
using System.Text;
using Helpdesk.Core.Tickets;
using Helpdesk.IntegrationTests.Infrastructure;

namespace Helpdesk.IntegrationTests;

public class AttachmentTests(SqlServerFixture sql) : WebTestBase(sql)
{
    private const char Rlo = (char)0x202E;
    private static readonly byte[] Content = Encoding.UTF8.GetBytes("printer log line 1\nprinter log line 2\n");

    private static readonly byte[] PdfContent = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\n");

    private static readonly byte[] PngContent = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

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
        await customer.UploadAttachmentAsync(id, "report.pdf", PdfContent);

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
        PdfContent.CopyTo(tooBig, 0);
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

    [Theory]
    [InlineData("holiday.png")]
    [InlineData("holiday.jpg")]
    [InlineData("contract.pdf")]
    [InlineData("sheet.xlsx")]
    public async Task A_program_renamed_to_look_like_a_document_is_refused(string name)
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Disguised file");
        var program = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };

        var html = await FollowedHtmlAsync(customer, await customer.UploadAttachmentAsync(id, name, program));

        Assert.Contains("does not match its type", html);
        Assert.Contains("No files attached", html);
    }

    [Fact]
    public async Task A_text_file_with_binary_content_is_refused()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Binary as text");

        var html = await FollowedHtmlAsync(customer, await customer.UploadAttachmentAsync(id, "notes.txt", [0x41, 0x00, 0x42, 0x00]));

        Assert.Contains("does not match its type", html);
    }

    [Fact]
    public async Task A_genuine_picture_is_accepted()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("A real picture");

        var html = await FollowedHtmlAsync(customer, await customer.UploadAttachmentAsync(id, "screen.png", PngContent));

        Assert.Contains("screen.png", html);
        Assert.DoesNotContain("does not match", html);
    }

    [Fact]
    public async Task The_direction_override_trick_in_a_file_name_is_removed()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("Tricky name");

        var html = await FollowedHtmlAsync(customer, await customer.UploadAttachmentAsync(id, "invoice" + Rlo + "fdp.txt", Content));

        Assert.DoesNotContain(Rlo.ToString(), html, StringComparison.Ordinal);
        Assert.Contains("invoicefdp.txt", html);
    }

    [Fact]
    public async Task Downloads_cannot_run_anything_in_the_browser()
    {
        var customer = await SignedInAsync("Customer");
        var id = await customer.CreateTicketAsync("With a file");
        var html = await FollowedHtmlAsync(customer, await customer.UploadAttachmentAsync(id, "notes.txt", Content));
        var link = System.Text.RegularExpressions.Regex.Match(html, "href=\"([^\"]*handler=Download[^\"]*)\"").Groups[1].Value.Replace("&amp;", "&", StringComparison.Ordinal);

        var download = await customer.GetAsync(link);

        Assert.Equal(System.Net.HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("default-src 'none'; sandbox", download.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("no-store", download.Headers.CacheControl?.ToString());
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

using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;

namespace Helpdesk.Core.Tests;

public class AttachmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    private static readonly Actor Customer = new("customer-1", UserRole.Customer);
    private static readonly Actor OtherCustomer = new("customer-2", UserRole.Customer);
    private static readonly Actor Operator = new("operator-1", UserRole.Operator);

    private static Ticket NewTicket() => Ticket.Create("Printer is down", "It prints blank pages.", TicketPriority.Normal, Customer, Now);

    [Fact]
    public void Owner_can_attach_a_file_and_the_change_is_recorded()
    {
        var ticket = NewTicket();

        var attachment = ticket.AddAttachment(Customer, "screenshot.PNG", 2048, "stored-1", Now.AddMinutes(5));

        Assert.Single(ticket.Attachments);
        Assert.Equal("screenshot.PNG", attachment.FileName);
        Assert.Equal("image/png", attachment.ContentType);
        Assert.Equal(2048, attachment.Size);
        Assert.Equal(Customer.Id, attachment.UploadedById);
        Assert.Equal(Now.AddMinutes(5), ticket.UpdatedAt);
        Assert.Contains(ticket.History, h => h.Field == "Attachment" && h.NewValue == "screenshot.PNG");
    }

    [Fact]
    public void Staff_can_attach_a_file_to_a_ticket_of_any_customer()
    {
        var ticket = NewTicket();

        ticket.AddAttachment(Operator, "report.pdf", 100, "stored-1", Now);

        Assert.Single(ticket.Attachments);
    }

    [Fact]
    public void Another_customer_cannot_attach_a_file()
    {
        var ticket = NewTicket();

        Assert.Throws<DomainException>(() => ticket.AddAttachment(OtherCustomer, "a.txt", 10, "stored-1", Now));
    }

    [Fact]
    public void Closed_ticket_rejects_files()
    {
        var ticket = NewTicket();
        ticket.ChangeStatus(Customer, TicketStatus.Closed, Now);

        Assert.Throws<DomainException>(() => ticket.AddAttachment(Customer, "a.txt", 10, "stored-1", Now));
    }

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("script.js")]
    [InlineData("archive.zip")]
    [InlineData("no-extension")]
    public void Unsupported_file_types_are_rejected(string fileName)
    {
        var ticket = NewTicket();

        var ex = Assert.Throws<DomainException>(() => ticket.AddAttachment(Customer, fileName, 10, "stored-1", Now));

        Assert.Contains("not accepted", ex.Message);
        Assert.Empty(ticket.Attachments);
    }

    [Fact]
    public void Empty_file_is_rejected()
    {
        var ticket = NewTicket();

        Assert.Throws<DomainException>(() => ticket.AddAttachment(Customer, "a.txt", 0, "stored-1", Now));
    }

    [Fact]
    public void File_over_the_size_limit_is_rejected_and_the_limit_itself_is_allowed()
    {
        var ticket = NewTicket();

        Assert.Throws<DomainException>(() => ticket.AddAttachment(Customer, "big.pdf", AttachmentPolicy.MaxFileBytes + 1, "stored-1", Now));

        ticket.AddAttachment(Customer, "exact.pdf", AttachmentPolicy.MaxFileBytes, "stored-2", Now);
        Assert.Single(ticket.Attachments);
    }

    [Fact]
    public void Ticket_accepts_a_limited_number_of_files()
    {
        var ticket = NewTicket();

        for (var i = 0; i < AttachmentPolicy.MaxFilesPerTicket; i++)
        {
            ticket.AddAttachment(Customer, $"file{i}.txt", 10, $"stored-{i}", Now);
        }

        var ex = Assert.Throws<DomainException>(() => ticket.AddAttachment(Customer, "one-more.txt", 10, "stored-x", Now));
        Assert.Contains("at most", ex.Message);
    }

    [Theory]
    [InlineData("..\\..\\windows\\notes.txt", "notes.txt")]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData("C:\\temp\\log.log", "log.log")]
    [InlineData("  spaced name.csv  ", "spaced name.csv")]
    public void File_name_is_reduced_to_its_plain_name(string input, string expected)
    {
        var ticket = NewTicket();

        var attachment = ticket.AddAttachment(Customer, input, 10, "stored-1", Now);

        Assert.Equal(expected, attachment.FileName);
    }

    [Fact]
    public void Overlong_file_name_is_shortened_and_keeps_its_extension()
    {
        var ticket = NewTicket();

        var attachment = ticket.AddAttachment(Customer, new string('a', 500) + ".pdf", 10, "stored-1", Now);

        Assert.Equal(AttachmentPolicy.MaxFileNameLength, attachment.FileName.Length);
        Assert.EndsWith(".pdf", attachment.FileName);
    }
}

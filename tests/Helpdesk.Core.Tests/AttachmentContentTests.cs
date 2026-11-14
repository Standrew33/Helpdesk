using System.Text;
using Helpdesk.Core.Tickets;

namespace Helpdesk.Core.Tests;

public class AttachmentContentTests
{
    private const char Rlo = (char)0x202E;
    private const char ZeroWidthSpace = (char)0x200B;
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
    private static readonly byte[] Gif = Encoding.ASCII.GetBytes("GIF89a\x01\x00");
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n");
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00];
    private static readonly byte[] Program = [0x4D, 0x5A, 0x90, 0x00];

    [Theory]
    [InlineData("a.png")]
    [InlineData("A.PNG")]
    public void A_png_needs_the_png_signature(string name)
    {
        Assert.True(AttachmentPolicy.ContentMatchesName(name, Png));
        Assert.False(AttachmentPolicy.ContentMatchesName(name, Program));
        Assert.False(AttachmentPolicy.ContentMatchesName(name, Jpeg));
    }

    [Fact]
    public void Each_type_is_recognised_by_its_own_signature()
    {
        Assert.True(AttachmentPolicy.ContentMatchesName("a.jpg", Jpeg));
        Assert.True(AttachmentPolicy.ContentMatchesName("a.jpeg", Jpeg));
        Assert.True(AttachmentPolicy.ContentMatchesName("a.gif", Gif));
        Assert.True(AttachmentPolicy.ContentMatchesName("a.pdf", Pdf));
        Assert.True(AttachmentPolicy.ContentMatchesName("a.docx", Zip));
        Assert.True(AttachmentPolicy.ContentMatchesName("a.xlsx", Zip));
        Assert.False(AttachmentPolicy.ContentMatchesName("a.pdf", Png));
        Assert.False(AttachmentPolicy.ContentMatchesName("a.docx", Pdf));
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.log")]
    [InlineData("a.csv")]
    public void Text_files_may_hold_text_but_not_binary_data(string name)
    {
        Assert.True(AttachmentPolicy.ContentMatchesName(name, Encoding.UTF8.GetBytes("line one\r\nline\ttwo\n")));
        Assert.True(AttachmentPolicy.ContentMatchesName(name, []));
        Assert.False(AttachmentPolicy.ContentMatchesName(name, [0x41, 0x00, 0x42]));
        Assert.False(AttachmentPolicy.ContentMatchesName(name, Program.Concat(new byte[] { 0x01 }).ToArray()));
    }

    [Fact]
    public void An_unknown_extension_never_matches()
        => Assert.False(AttachmentPolicy.ContentMatchesName("a.exe", Program));

    [Fact]
    public void The_text_direction_override_is_removed_from_file_names()
    {
        var name = AttachmentPolicy.CleanFileName("invoice" + Rlo + "fdp.exe");

        Assert.Equal("invoicefdp.exe", name);
    }

    [Fact]
    public void Zero_width_characters_are_removed_from_file_names()
        => Assert.Equal("report.pdf", AttachmentPolicy.CleanFileName("rep" + ZeroWidthSpace + "ort.pdf"));
}

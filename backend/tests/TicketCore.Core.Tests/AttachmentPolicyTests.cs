using System.Text;
using TicketCore.Common;
using TicketCore.Core.Tickets;

namespace TicketCore.Core.Tests;

public class AttachmentPolicyTests
{
    private static readonly StorageOptions Limits = new() { MaxFileBytes = 1024, MaxFilesPerUpload = 5 };
    private static readonly byte[] PdfHead = "%PDF-1.7"u8.ToArray();
    private static readonly byte[] PngHead = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] ExeHead = "MZ\u0090\0\u0003\0\0\0"u8.ToArray();

    [Fact]
    public void Real_pdf_is_accepted_with_its_content_type()
    {
        var (type, error) = AttachmentPolicy.Check("report.pdf", 500, PdfHead, Limits);
        Assert.Null(error);
        Assert.Equal("application/pdf", type);
    }

    [Fact]
    public void Executable_renamed_to_pdf_is_rejected()
    {
        var (_, error) = AttachmentPolicy.Check("invoice.pdf", 500, ExeHead, Limits);
        Assert.Contains("doesn't look like a real PDF", error);
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("page.html")]
    [InlineData("script.js")]
    [InlineData("image.svg")]
    [InlineData("noextension")]
    public void Dangerous_or_unknown_types_are_rejected(string name)
    {
        var (_, error) = AttachmentPolicy.Check(name, 10, PngHead, Limits);
        Assert.Contains("can't be attached", error);
    }

    [Fact]
    public void Oversized_file_is_rejected()
    {
        var (_, error) = AttachmentPolicy.Check("big.png", Limits.MaxFileBytes + 1, PngHead, Limits);
        Assert.NotNull(error);
    }

    [Fact]
    public void Text_file_with_binary_content_is_rejected()
    {
        var (_, okError) = AttachmentPolicy.Check("notes.txt", 10, Encoding.UTF8.GetBytes("hello wo"), Limits);
        Assert.Null(okError);

        var (_, error) = AttachmentPolicy.Check("notes.txt", 10, ExeHead, Limits);
        Assert.Contains("doesn't look like a text file", error);
    }

    [Fact]
    public void Extension_check_ignores_case()
    {
        Assert.Null(AttachmentPolicy.Check("PHOTO.PNG", 10, PngHead, Limits).Error);
    }

    [Theory]
    [InlineData(@"C:\fakepath\report.pdf", "report.pdf")]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData("na\"me\r\n.pdf", "name.pdf")]
    public void Safe_name_strips_paths_and_control_characters(string input, string expected)
    {
        Assert.Equal(expected, AttachmentPolicy.SafeName(input));
    }
}

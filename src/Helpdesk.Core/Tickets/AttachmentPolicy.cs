namespace Helpdesk.Core.Tickets;

/// <summary>Which files a ticket accepts. The content type is derived from the extension, never taken from the client.</summary>
public static class AttachmentPolicy
{
    public const int MaxFilesPerTicket = 5;
    public const long MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxFileNameLength = 200;

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".pdf"] = "application/pdf",
        [".txt"] = "text/plain",
        [".log"] = "text/plain",
        [".csv"] = "text/csv",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    public static IReadOnlyCollection<string> AllowedExtensions => ContentTypes.Keys;

    /// <summary>The content type for an allowed file name, or null when the extension is not accepted.</summary>
    public static string? ContentTypeFor(string fileName)
        => ContentTypes.TryGetValue(Path.GetExtension(fileName), out var contentType) ? contentType : null;

    /// <summary>How many leading bytes of a file are needed to recognise its type.</summary>
    public const int HeaderLength = 4096;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>
    /// Whether the start of a file is what its extension promises. Images, PDF and Office files are recognised by their
    /// signature; text files must not contain binary data. This stops a program from being passed off as a picture.
    /// </summary>
    public static bool ContentMatchesName(string fileName, ReadOnlySpan<byte> header)
    {
        switch (Path.GetExtension(fileName).ToLowerInvariant())
        {
            case ".png":
                return header.StartsWith(PngSignature);
            case ".jpg" or ".jpeg":
                return header.StartsWith(JpegSignature);
            case ".gif":
                return header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8);
            case ".pdf":
                return header.StartsWith(PdfSignature);
            case ".docx" or ".xlsx":
                return header.StartsWith(ZipSignature);
            case ".txt" or ".log" or ".csv":
                // Text has no zero bytes and no other control characters except tab, line breaks and escape.
                foreach (var b in header)
                {
                    if (b < 0x20 && b is not (0x09 or 0x0A or 0x0D or 0x0C or 0x1B))
                    {
                        return false;
                    }
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>Keeps the file name only: no directories, no control or invisible characters, limited length.</summary>
    public static string CleanFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')).Trim();

        // Format characters include the right-to-left override that makes "invoice-fdp.exe" look like "invoice-exe.pdf".
        name = new string(name.Where(c => !char.IsControl(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray());

        if (name.Length > MaxFileNameLength)
        {
            var extension = Path.GetExtension(name);
            name = name[..(MaxFileNameLength - extension.Length)] + extension;
        }

        return name;
    }
}

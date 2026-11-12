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

    /// <summary>Keeps the file name only: no directories, no control characters, limited length.</summary>
    public static string CleanFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')).Trim();
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());

        if (name.Length > MaxFileNameLength)
        {
            var extension = Path.GetExtension(name);
            name = name[..(MaxFileNameLength - extension.Length)] + extension;
        }

        return name;
    }
}

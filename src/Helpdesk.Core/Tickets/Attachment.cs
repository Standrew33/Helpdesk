namespace Helpdesk.Core.Tickets;

/// <summary>Metadata of a file attached to a ticket. The content itself lives in file storage under <see cref="StoredName"/>.</summary>
public sealed class Attachment
{
    internal Attachment(string fileName, string contentType, long size, string storedName, string uploadedById, DateTimeOffset uploadedAt)
    {
        FileName = fileName;
        ContentType = contentType;
        Size = size;
        StoredName = storedName;
        UploadedById = uploadedById;
        UploadedAt = uploadedAt;
    }

    // Required by EF Core.
    private Attachment()
    {
        FileName = string.Empty;
        ContentType = string.Empty;
        StoredName = string.Empty;
        UploadedById = string.Empty;
    }

    public int Id { get; private set; }

    public int TicketId { get; private set; }

    public string FileName { get; private set; }

    public string ContentType { get; private set; }

    public long Size { get; private set; }

    /// <summary>Name of the stored file; a random value, so file names cannot be guessed or clash.</summary>
    public string StoredName { get; private set; }

    public string UploadedById { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }
}

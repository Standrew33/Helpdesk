namespace Helpdesk.Core.Tickets;

public sealed class Comment
{
    internal Comment(string authorId, string body, bool isInternal, DateTimeOffset createdAt)
    {
        AuthorId = authorId;
        Body = body;
        IsInternal = isInternal;
        CreatedAt = createdAt;
    }

    // Required by EF Core.
    private Comment()
    {
        AuthorId = string.Empty;
        Body = string.Empty;
    }

    public int Id { get; private set; }

    public int TicketId { get; private set; }

    public string AuthorId { get; private set; }

    public string Body { get; private set; }

    /// <summary>Internal notes are visible to staff only.</summary>
    public bool IsInternal { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

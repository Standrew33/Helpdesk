namespace Helpdesk.Core.Tickets;

public sealed class Comment
{
    internal Comment(string authorId, string body, bool isInternal, DateTimeOffset createdAt, bool isClosingReason = false)
    {
        AuthorId = authorId;
        Body = body;
        IsInternal = isInternal;
        CreatedAt = createdAt;
        IsClosingReason = isClosingReason;
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

    /// <summary>The reason a customer gave for closing the ticket.</summary>
    public bool IsClosingReason { get; private set; }
}

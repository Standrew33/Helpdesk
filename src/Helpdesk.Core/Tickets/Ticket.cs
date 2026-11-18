using Helpdesk.Core.Users;

namespace Helpdesk.Core.Tickets;

public sealed class Ticket
{
    public const int MaxSubjectLength = 200;
    public const int MaxDescriptionLength = 4000;
    public const int MaxCommentLength = 4000;

    private readonly List<Comment> _comments = [];
    private readonly List<Attachment> _attachments = [];
    private readonly List<TicketHistoryEntry> _history = [];

    // Required by EF Core.
    private Ticket()
    {
        Subject = string.Empty;
        Description = string.Empty;
        CustomerId = string.Empty;
    }

    public int Id { get; private set; }

    public int WorkspaceId { get; private set; }

    /// <summary>The number people see; it counts from 1 within the workspace.</summary>
    public int Number { get; private set; }

    public string Subject { get; private set; }

    public string Description { get; private set; }

    public TicketStatus Status { get; private set; }

    public TicketPriority Priority { get; private set; }

    public string CustomerId { get; private set; }

    public string? AssigneeId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Concurrency token, maintained by the database.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<Comment> Comments => _comments;

    public IReadOnlyList<Attachment> Attachments => _attachments;

    public IReadOnlyList<TicketHistoryEntry> History => _history;

    /// <summary>Opens a ticket in the customer's workspace, under the number the workspace gave out for it.</summary>
    public static Ticket Create(int number, string subject, string description, TicketPriority priority, Actor customer, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customer.WorkspaceId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);

        if (customer.Role != UserRole.Customer)
        {
            throw new DomainException("Only customers can open tickets.");
        }

        if (priority == TicketPriority.Urgent)
        {
            throw new DomainException("Only staff can set urgent priority.");
        }

        var ticket = new Ticket
        {
            WorkspaceId = customer.WorkspaceId,
            Number = number,
            Subject = RequireText(subject, "Subject", MaxSubjectLength),
            Description = OptionalText(description, "Description", MaxDescriptionLength),
            Status = TicketStatus.New,
            Priority = priority,
            CustomerId = customer.Id,
            CreatedAt = now,
            UpdatedAt = now,
            DueAt = SlaPolicy.CalculateDueAt(priority, now),
        };

        ticket.Record(customer, "Created", null, null, now);
        return ticket;
    }

    /// <summary>Staff see the tickets of their own workspace, a customer only their own tickets; nobody sees past the workspace.</summary>
    public bool CanBeViewedBy(Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.WorkspaceId == WorkspaceId && (actor.Role.IsStaff() || actor.Id == CustomerId);
    }

    /// <summary>Comments the actor is allowed to see: customers do not see internal notes.</summary>
    public IEnumerable<Comment> CommentsVisibleTo(Actor actor)
    {
        EnsureCanView(actor);
        return actor.Role.IsStaff() ? _comments : _comments.Where(c => !c.IsInternal);
    }

    public void ChangeStatus(Actor actor, TicketStatus target, DateTimeOffset now)
    {
        EnsureCanView(actor);

        if (target == Status)
        {
            throw new DomainException($"Ticket is already {Status}.");
        }

        if (!TicketWorkflow.IsAllowed(Status, target, actor.Role))
        {
            throw new DomainException($"Changing status from {Status} to {target} is not allowed.");
        }

        SetStatus(actor, target, now);
    }

    public void Assign(Actor actor, string? assigneeId, DateTimeOffset now)
    {
        EnsureCanView(actor);
        RequireStaff(actor, "assign tickets");
        EnsureNotClosed();

        if (assigneeId == AssigneeId)
        {
            throw new DomainException("Ticket is already assigned to this user.");
        }

        Record(actor, "Assignee", AssigneeId, assigneeId, now);
        AssigneeId = assigneeId;
        UpdatedAt = now;
    }

    /// <summary>
    /// Changes what the ticket says and how pressing it is. The customer who opened it and staff may edit an open
    /// ticket; only staff may make it urgent. The description may be left empty. Each changed field is recorded; nothing changed means nothing recorded.
    /// </summary>
    public void Edit(Actor actor, string subject, string description, TicketPriority priority, DateTimeOffset now)
    {
        EnsureCanView(actor);
        EnsureNotClosed();

        var newSubject = RequireText(subject, "Subject", MaxSubjectLength);
        var newDescription = OptionalText(description, "Description", MaxDescriptionLength);

        if (priority == TicketPriority.Urgent && Priority != TicketPriority.Urgent && !actor.Role.IsStaff())
        {
            throw new DomainException("Only staff can set urgent priority.");
        }

        var changed = false;

        if (newSubject != Subject)
        {
            Record(actor, "Subject", Subject, newSubject, now);
            Subject = newSubject;
            changed = true;
        }

        // The text itself is not copied into the history; the entry says that it was edited, and by whom.
        if (newDescription != Description)
        {
            Record(actor, "Description", null, null, now);
            Description = newDescription;
            changed = true;
        }

        if (priority != Priority)
        {
            Record(actor, "Priority", Priority.ToString(), priority.ToString(), now);
            Priority = priority;
            DueAt = SlaPolicy.CalculateDueAt(priority, CreatedAt);
            changed = true;
        }

        if (changed)
        {
            UpdatedAt = now;
        }
    }

    public Comment AddComment(Actor actor, string body, bool isInternal, DateTimeOffset now)
    {
        EnsureCanView(actor);
        EnsureNotClosed();

        if (isInternal && !actor.Role.IsStaff())
        {
            throw new DomainException("Only staff can add internal notes.");
        }

        var comment = new Comment(actor.Id, RequireText(body, "Comment", MaxCommentLength), isInternal, now);
        _comments.Add(comment);
        UpdatedAt = now;

        // A reply from the customer puts the ticket back to work.
        if (Status == TicketStatus.WaitingForCustomer && actor.Role == UserRole.Customer)
        {
            SetStatus(actor, TicketStatus.InProgress, now);
        }

        return comment;
    }

    /// <summary>
    /// Records a file attached to the ticket. <paramref name="storedName"/> is where the content was
    /// (or will be) saved; the caller keeps the two in step.
    /// </summary>
    public Attachment AddAttachment(Actor actor, string? fileName, long size, string storedName, DateTimeOffset now)
    {
        EnsureCanView(actor);
        EnsureNotClosed();

        var name = AttachmentPolicy.CleanFileName(fileName);

        if (name.Length == 0)
        {
            throw new DomainException("The file has no name.");
        }

        var contentType = AttachmentPolicy.ContentTypeFor(name)
            ?? throw new DomainException(
                $"Files of this type are not accepted. Allowed: {string.Join(", ", AttachmentPolicy.AllowedExtensions.Order())}.");

        if (size <= 0)
        {
            throw new DomainException("The file is empty.");
        }

        if (size > AttachmentPolicy.MaxFileBytes)
        {
            throw new DomainException($"The file is larger than {AttachmentPolicy.MaxFileBytes / (1024 * 1024)} MB.");
        }

        if (_attachments.Count >= AttachmentPolicy.MaxFilesPerTicket)
        {
            throw new DomainException($"A ticket can have at most {AttachmentPolicy.MaxFilesPerTicket} files.");
        }

        var attachment = new Attachment(name, contentType, size, storedName, actor.Id, now);
        _attachments.Add(attachment);
        Record(actor, "Attachment", null, name, now);
        UpdatedAt = now;
        return attachment;
    }

    /// <summary>True when the ticket is not resolved in time, or was closed after its deadline.</summary>
    public bool IsSlaBreached(DateTimeOffset now) => (ClosedAt ?? now) > DueAt;

    private void SetStatus(Actor actor, TicketStatus target, DateTimeOffset now)
    {
        Record(actor, "Status", Status.ToString(), target.ToString(), now);
        Status = target;
        ClosedAt = target == TicketStatus.Closed ? now : null;
        UpdatedAt = now;
    }

    private void Record(Actor actor, string field, string? oldValue, string? newValue, DateTimeOffset now)
        => _history.Add(new TicketHistoryEntry(actor.Id, field, oldValue, newValue, now));

    private void EnsureCanView(Actor actor)
    {
        if (!CanBeViewedBy(actor))
        {
            throw new DomainException("The user has no access to this ticket.");
        }
    }

    private void EnsureNotClosed()
    {
        if (Status == TicketStatus.Closed)
        {
            throw new DomainException("The ticket is closed. Reopen it first.");
        }
    }

    private static void RequireStaff(Actor actor, string action)
    {
        if (!actor.Role.IsStaff())
        {
            throw new DomainException($"Only staff can {action}.");
        }
    }

    private static string RequireText(string? value, string name, int maxLength)
    {
        var text = OptionalText(value, name, maxLength);

        if (text.Length == 0)
        {
            throw new DomainException($"{name} is required.");
        }

        return text;
    }

    /// <summary>Trimmed text that may be left empty, though not too long.</summary>
    private static string OptionalText(string? value, string name, int maxLength)
    {
        var text = value?.Trim() ?? string.Empty;

        if (text.Length > maxLength)
        {
            throw new DomainException($"{name} must be at most {maxLength} characters.");
        }

        return text;
    }
}

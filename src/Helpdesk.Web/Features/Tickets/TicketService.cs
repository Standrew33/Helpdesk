using Helpdesk.Core;
using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Attachments;
using Helpdesk.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Features.Tickets;

public class TicketService(
    HelpdeskDbContext db,
    IAttachmentStore attachments,
    TimeProvider time,
    ILogger<TicketService> logger)
{
    public const int PageSize = 15;

    private const int MaxSearchLength = 100;

    public async Task<PagedList<TicketListItem>> ListAsync(Actor actor, TicketFilter filter, CancellationToken ct = default)
    {
        var today = Ticket.Today(time.GetUtcNow());
        var tickets = db.Tickets.AsNoTracking().Where(t => t.WorkspaceId == actor.WorkspaceId);

        if (!actor.Role.IsStaff())
        {
            tickets = tickets.Where(t => t.CustomerId == actor.Id);
        }

        if (filter.Statuses is { Count: > 0 } statuses)
        {
            tickets = tickets.Where(t => statuses.Contains(t.Status));
        }

        if (filter.Priorities is { Count: > 0 } priorities)
        {
            tickets = tickets.Where(t => priorities.Contains(t.Priority));
        }

        // The queue views are for staff; a customer's list is already limited to their own tickets.
        if (filter.Scopes is { Count: > 0 } scopes && actor.Role.IsStaff())
        {
            var mine = scopes.Contains(TicketScope.Mine);
            var unassigned = scopes.Contains(TicketScope.Unassigned);
            var overdue = scopes.Contains(TicketScope.Overdue);

            tickets = tickets.Where(t =>
                (mine && t.AssigneeId == actor.Id)
                || (unassigned && t.AssigneeId == null)
                || (overdue && t.Status != TicketStatus.Closed && t.DueDate < today));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            term = term.Length > MaxSearchLength ? term[..MaxSearchLength] : term;

            // "#42" or "42" finds ticket 42 of the workspace; any text is looked up in the subject and the description.
            var isNumber = int.TryParse(term.TrimStart('#'), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number);
            tickets = tickets.Where(t => t.Subject.Contains(term) || t.Description.Contains(term) || (isNumber && t.Number == number));
        }

        var total = await tickets.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        var page = Math.Clamp(filter.Page, 1, pageCount);

        var rows = await Sorted(
            from t in tickets
            join c in db.Users on t.CustomerId equals c.Id
            join a in db.Users on t.AssigneeId equals a.Id into assignees
            from a in assignees.DefaultIfEmpty()
            select new ListRow
            {
                Number = t.Number,
                Subject = t.Subject,
                Status = t.Status,
                Priority = t.Priority,
                CustomerName = c.DisplayName,
                AssigneeName = a.DisplayName,
                DueDate = t.DueDate,
                UpdatedAt = t.UpdatedAt,
            },
            filter.Sort,
            filter.Descending)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(ct);

        var items = rows
            .Select(r => new TicketListItem(
                r.Number, r.Subject, r.Status, r.Priority, r.CustomerName, r.AssigneeName, r.DueDate,
                IsOverdue: r.Status != TicketStatus.Closed && r.DueDate < today))
            .ToList();

        return new PagedList<TicketListItem>(items, total, page, PageSize);
    }

    public async Task<TicketDetails?> GetAsync(int number, Actor actor, CancellationToken ct = default)
    {
        var ticket = await InWorkspace(db.Tickets.AsNoTracking(), actor, number)
            .Include(t => t.Comments)
            .Include(t => t.Attachments)
            .Include(t => t.History)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct);

        // A ticket the user may not see looks the same as a ticket that does not exist.
        if (ticket is null || !CanSee(ticket, actor))
        {
            return null;
        }

        var comments = ticket.CommentsVisibleTo(actor).OrderBy(c => c.CreatedAt).ToList();

        var userIds = new HashSet<string> { ticket.CustomerId };
        if (ticket.AssigneeId is not null)
        {
            userIds.Add(ticket.AssigneeId);
        }

        userIds.UnionWith(comments.Select(c => c.AuthorId));
        userIds.UnionWith(ticket.Attachments.Select(a => a.UploadedById));
        userIds.UnionWith(ticket.History.Select(h => h.ActorId));

        // Assignee changes store user ids as values; resolve them too.
        userIds.UnionWith(ticket.History.Where(h => h.Field == "Assignee")
            .SelectMany(h => new[] { h.OldValue, h.NewValue })
            .OfType<string>());

        var names = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return new TicketDetails(ticket, comments, names);
    }

    public async Task<IReadOnlyList<AssigneeOption>> GetAssigneeOptionsAsync(Actor actor, CancellationToken ct = default)
        => await AssignableStaff(actor)
            .OrderBy(u => u.DisplayName)
            .Select(u => new AssigneeOption(u.Id, u.DisplayName))
            .ToListAsync(ct);

    /// <summary>The people a ticket can be given to: active operators and admins of the actor's own workspace.</summary>
    private IQueryable<ApplicationUser> AssignableStaff(Actor actor)
    {
        string[] staffRoles = [RoleNames.Operator, RoleNames.Admin];

        return db.Users.Where(u =>
            u.WorkspaceId == actor.WorkspaceId
            && u.IsActive
            && db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && staffRoles.Contains(r.Name!))));
    }

    /// <summary>Creates a ticket; throws <see cref="DomainException"/> when the input breaks a rule.</summary>
    /// <returns>The number of the new ticket in the actor's workspace.</returns>
    public async Task<int> CreateAsync(Actor actor, string subject, string description, TicketPriority priority, DateOnly? dueDate = null)
    {
        // The number and the ticket are saved together: a ticket that is refused does not use up a number.
        Ticket? ticket = null;
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            // A retried attempt starts over; the ticket of the failed one must not be saved twice.
            if (ticket is not null)
            {
                db.Entry(ticket).State = EntityState.Detached;
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            var number = await TicketNumbers.ReserveAsync(db, actor.WorkspaceId);
            ticket = Ticket.Create(number, subject, description, priority, actor, time.GetUtcNow(), dueDate);
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        return ticket!.Number;
    }

    /// <summary>Changes the status; a customer closing their ticket gives the <paramref name="reason"/>.</summary>
    public Task<ServiceResult> ChangeStatusAsync(int number, Actor actor, string? rowVersion, TicketStatus status, string? reason = null)
        => UpdateAsync(number, actor, rowVersion, (ticket, now) => ticket.ChangeStatus(actor, status, now, reason));

    /// <summary>Changes the fields that are given and keeps the others as they are.</summary>
    public Task<ServiceResult> EditAsync(
        int number, Actor actor, string? rowVersion, string? subject = null, string? description = null, TicketPriority? priority = null)
        => UpdateAsync(number, actor, rowVersion, (ticket, now) => ticket.Edit(
            actor, subject ?? ticket.Subject, description ?? ticket.Description, priority ?? ticket.Priority, now));

    /// <summary>Sets the due date of a ticket, or removes it with null.</summary>
    public Task<ServiceResult> SetDueDateAsync(int number, Actor actor, string? rowVersion, DateOnly? dueDate)
        => UpdateAsync(number, actor, rowVersion, (ticket, now) => ticket.SetDueDate(actor, dueDate, now));

    public Task<ServiceResult> AddCommentAsync(int number, Actor actor, string? rowVersion, string body, bool isInternal)
        => UpdateAsync(number, actor, rowVersion, (ticket, now) => ticket.AddComment(actor, body, isInternal, now));

    /// <summary>
    /// Attaches a file: the rules are checked first, then the content is stored, then the ticket is saved.
    /// When the save fails the stored content is removed again, so storage never keeps files no ticket knows about.
    /// </summary>
    public async Task<ServiceResult> AddAttachmentAsync(
        int number, Actor actor, string? rowVersion, string? fileName, long length, Stream content, CancellationToken ct = default)
    {
        string? storedName = null;

        var result = await UpdateAsync(number, actor, rowVersion, async (ticket, now) =>
        {
            var name = Guid.NewGuid().ToString("N");
            var attachment = ticket.AddAttachment(actor, fileName, length, name, now);
            await EnsureContentMatchesNameAsync(ticket.Id, fileName, content, ct);
            await attachments.SaveAsync(name, content, attachment.ContentType, ct);
            storedName = name;
        });

        if (!result.Succeeded && storedName is not null)
        {
            await attachments.DeleteAsync(storedName, CancellationToken.None);
        }

        return result;
    }

    /// <summary>Opens an attachment the actor may see; null when the ticket or file is not available to them.</summary>
    public async Task<AttachmentDownload?> OpenAttachmentAsync(int number, int attachmentId, Actor actor, CancellationToken ct = default)
    {
        var ticket = await InWorkspace(db.Tickets.AsNoTracking(), actor, number)
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(ct);

        var attachment = ticket is not null && CanSee(ticket, actor)
            ? ticket.Attachments.FirstOrDefault(a => a.Id == attachmentId)
            : null;

        if (attachment is null)
        {
            return null;
        }

        var stream = await attachments.OpenReadAsync(attachment.StoredName, ct);
        return stream is null ? null : new AttachmentDownload(attachment.FileName, attachment.ContentType, stream);
    }

    public async Task<ServiceResult> AssignAsync(int number, Actor actor, string? rowVersion, string? assigneeId)
    {
        if (!string.IsNullOrEmpty(assigneeId))
        {
            if (!await AssignableStaff(actor).AnyAsync(u => u.Id == assigneeId))
            {
                return ServiceResult.Invalid("Tickets can be assigned to active operators and admins of this help desk only.");
            }
        }

        var target = string.IsNullOrEmpty(assigneeId) ? null : assigneeId;
        return await UpdateAsync(number, actor, rowVersion, (ticket, now) => ticket.Assign(actor, target, now));
    }

    private Task<ServiceResult> UpdateAsync(int number, Actor actor, string? rowVersion, Action<Ticket, DateTimeOffset> change)
        => UpdateAsync(number, actor, rowVersion, (ticket, now) =>
        {
            change(ticket, now);
            return Task.CompletedTask;
        });

    private async Task<ServiceResult> UpdateAsync(int number, Actor actor, string? rowVersion, Func<Ticket, DateTimeOffset, Task> change)
    {
        var ticket = await InWorkspace(db.Tickets, actor, number).Include(t => t.Attachments).FirstOrDefaultAsync();

        if (ticket is null || !CanSee(ticket, actor))
        {
            return ServiceResult.NotFound();
        }

        if (!TryParseRowVersion(rowVersion, out var version))
        {
            return ServiceResult.Invalid("The page is out of date. Reload it and try again.");
        }

        // Make EF compare against the version the user saw, not the one just loaded.
        db.Entry(ticket).Property(t => t.RowVersion).OriginalValue = version;

        try
        {
            await change(ticket, time.GetUtcNow());
            await db.SaveChangesAsync();
            return ServiceResult.Success();
        }
        catch (DomainException ex)
        {
            return ServiceResult.Invalid(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("The ticket was changed by someone else. Reload the page and try again.");
        }
    }

    /// <summary>A ticket is known by its number within the workspace; the same number in another workspace is another ticket.</summary>
    private static IQueryable<Ticket> InWorkspace(IQueryable<Ticket> tickets, Actor actor, int number)
        => tickets.Where(t => t.WorkspaceId == actor.WorkspaceId && t.Number == number);

    /// <summary>The domain rule on who may see a ticket; a refusal is logged.</summary>
    private bool CanSee(Ticket ticket, Actor actor)
    {
        var allowed = ticket.CanBeViewedBy(actor);

        if (!allowed)
        {
            SecurityLog.TicketAccessRefused(logger, actor.Id, ticket.Id);
        }

        return allowed;
    }

    /// <summary>A file is accepted by its name, but what the name claims must be true: an .exe renamed to .png is refused.</summary>
    private async Task EnsureContentMatchesNameAsync(int ticketId, string? fileName, Stream content, CancellationToken ct)
    {
        var name = AttachmentPolicy.CleanFileName(fileName);

        // Names with a type that is not accepted get the domain's own message.
        if (AttachmentPolicy.ContentTypeFor(name) is null)
        {
            return;
        }

        var header = new byte[AttachmentPolicy.HeaderLength];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        content.Position = 0;

        if (!AttachmentPolicy.ContentMatchesName(name, header.AsSpan(0, read)))
        {
            SecurityLog.UploadRefused(logger, ticketId);
            throw new DomainException("The content of the file does not match its type.");
        }
    }

    private static IQueryable<ListRow> Sorted(IQueryable<ListRow> rows, TicketSort sort, bool descending)
    {
        // Status and priority are stored as text; they are sorted by their place in the workflow and by urgency.
        IOrderedQueryable<ListRow> sorted = sort switch
        {
            TicketSort.Number => By(rows, r => r.Number, descending),
            TicketSort.Subject => By(rows, r => r.Subject, descending),
            TicketSort.Status => By(rows, r => r.Status == TicketStatus.New ? 0
                : r.Status == TicketStatus.InProgress ? 1
                : r.Status == TicketStatus.WaitingForCustomer ? 2
                : 3, descending),
            TicketSort.Priority => By(rows, r => r.Priority == TicketPriority.Urgent ? 4
                : r.Priority == TicketPriority.High ? 3
                : r.Priority == TicketPriority.Normal ? 2
                : r.Priority == TicketPriority.Low ? 1
                : 0, descending),
            TicketSort.Customer => By(rows, r => r.CustomerName, descending),
            TicketSort.Assignee => By(rows, r => r.AssigneeName, descending),
            // Tickets without a due date come after the dated ones, whichever way the dates run.
            TicketSort.Due => descending
                ? rows.OrderBy(r => r.DueDate == null).ThenByDescending(r => r.DueDate)
                : rows.OrderBy(r => r.DueDate == null).ThenBy(r => r.DueDate),
            _ => By(rows, r => r.UpdatedAt, descending),
        };

        // Equal values keep a stable order, so pages never overlap.
        return sorted.ThenByDescending(r => r.Number);
    }

    private static IOrderedQueryable<ListRow> By<TKey>(IQueryable<ListRow> rows, System.Linq.Expressions.Expression<Func<ListRow, TKey>> key, bool descending)
        => descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

    private static bool TryParseRowVersion(string? value, out byte[] version)
    {
        version = [];

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            version = Convert.FromBase64String(value);
            return version.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>One row of the ticket list as read from the database, before it is shown.</summary>
internal sealed class ListRow
{
    public int Number { get; init; }

    public string Subject { get; init; } = string.Empty;

    public TicketStatus Status { get; init; }

    public TicketPriority Priority { get; init; }

    public string CustomerName { get; init; } = string.Empty;

    public string? AssigneeName { get; init; }

    public DateOnly? DueDate { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

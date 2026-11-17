using Helpdesk.Core;
using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Attachments;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Features.Tickets;

public class TicketService(
    HelpdeskDbContext db,
    UserManager<ApplicationUser> userManager,
    IAttachmentStore attachments,
    TimeProvider time,
    ILogger<TicketService> logger)
{
    public const int PageSize = 15;

    private const int MaxSearchLength = 100;

    public async Task<PagedList<TicketListItem>> ListAsync(Actor actor, TicketFilter filter, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var tickets = db.Tickets.AsNoTracking().Where(t => t.WorkspaceId == actor.WorkspaceId);

        if (!actor.Role.IsStaff())
        {
            tickets = tickets.Where(t => t.CustomerId == actor.Id);
        }
        else if (actor.IsDemo)
        {
            // The shared demo staff work on demo data only; tickets of real visitors are out of their sight.
            tickets = tickets.Where(t => db.Users.Any(u => u.Id == t.CustomerId && u.Email!.EndsWith(DatabaseSeeder.DemoDomain)));
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
                || (overdue && t.Status != TicketStatus.Closed && t.DueAt < now));
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
                DueAt = t.DueAt,
                UpdatedAt = t.UpdatedAt,
            },
            filter.Sort,
            filter.Descending)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(ct);

        var items = rows
            .Select(r => new TicketListItem(
                r.Number, r.Subject, r.Status, r.Priority, r.CustomerName, r.AssigneeName, r.DueAt,
                IsOverdue: r.Status != TicketStatus.Closed && now > r.DueAt))
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
        if (ticket is null || !await CanSeeAsync(ticket, actor, ct))
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

    public async Task<IReadOnlyList<AssigneeOption>> GetAssigneeOptionsAsync()
    {
        var operators = await userManager.GetUsersInRoleAsync(RoleNames.Operator);
        var admins = await userManager.GetUsersInRoleAsync(RoleNames.Admin);

        return operators.Concat(admins)
            .Where(u => u.IsActive)
            .DistinctBy(u => u.Id)
            .OrderBy(u => u.DisplayName)
            .Select(u => new AssigneeOption(u.Id, u.DisplayName))
            .ToList();
    }

    /// <summary>Creates a ticket; throws <see cref="DomainException"/> when the input breaks a rule.</summary>
    /// <returns>The number of the new ticket in the actor's workspace.</returns>
    public async Task<int> CreateAsync(Actor actor, string subject, string description, TicketPriority priority)
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
            ticket = Ticket.Create(number, subject, description, priority, actor, time.GetUtcNow());
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        return ticket!.Number;
    }

    public Task<ServiceResult> ChangeStatusAsync(int number, Actor actor, string? rowVersion, TicketStatus status)
        => UpdateAsync(number, actor, rowVersion, (ticket, now) => ticket.ChangeStatus(actor, status, now));

    /// <summary>Changes the fields that are given and keeps the others as they are.</summary>
    public Task<ServiceResult> EditAsync(
        int number, Actor actor, string? rowVersion, string? subject = null, string? description = null, TicketPriority? priority = null)
        => UpdateAsync(number, actor, rowVersion, (ticket, now) => ticket.Edit(
            actor, subject ?? ticket.Subject, description ?? ticket.Description, priority ?? ticket.Priority, now));

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

        var attachment = ticket is not null && await CanSeeAsync(ticket, actor, ct)
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
            var assignee = await userManager.FindByIdAsync(assigneeId);

            if (assignee is not { IsActive: true }
                || !(await userManager.IsInRoleAsync(assignee, RoleNames.Operator)
                    || await userManager.IsInRoleAsync(assignee, RoleNames.Admin)))
            {
                return ServiceResult.Invalid("Tickets can be assigned to active operators and admins only.");
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

        if (ticket is null || !await CanSeeAsync(ticket, actor))
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

    /// <summary>The domain rule on who may see a ticket, plus the rule that demo staff never reach tickets of real visitors.</summary>
    private async Task<bool> CanSeeAsync(Ticket ticket, Actor actor, CancellationToken ct = default)
    {
        var allowed = ticket.CanBeViewedBy(actor);

        if (allowed && actor.IsDemo && actor.Role.IsStaff())
        {
            allowed = await db.Users.AnyAsync(u => u.Id == ticket.CustomerId && u.Email!.EndsWith(DatabaseSeeder.DemoDomain), ct);
        }

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
            TicketSort.Due => By(rows, r => r.DueAt, descending),
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

    public DateTimeOffset DueAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

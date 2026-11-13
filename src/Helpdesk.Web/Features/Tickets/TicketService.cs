using Helpdesk.Core;
using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Attachments;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Features.Tickets;

public class TicketService(HelpdeskDbContext db, UserManager<ApplicationUser> userManager, IAttachmentStore attachments, TimeProvider time)
{
    public const int PageSize = 15;

    private const int MaxSearchLength = 100;

    public async Task<PagedList<TicketListItem>> ListAsync(Actor actor, TicketFilter filter, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var tickets = db.Tickets.AsNoTracking();

        if (!actor.Role.IsStaff())
        {
            tickets = tickets.Where(t => t.CustomerId == actor.Id);
        }

        if (filter.Status is not null)
        {
            tickets = tickets.Where(t => t.Status == filter.Status);
        }

        if (filter.Priority is not null)
        {
            tickets = tickets.Where(t => t.Priority == filter.Priority);
        }

        // The queue views are for staff; a customer's list is already limited to their own tickets.
        if (filter.Scope is not null && actor.Role.IsStaff())
        {
            tickets = filter.Scope switch
            {
                TicketScope.Mine => tickets.Where(t => t.AssigneeId == actor.Id),
                TicketScope.Unassigned => tickets.Where(t => t.AssigneeId == null),
                _ => tickets.Where(t => t.Status != TicketStatus.Closed && t.DueAt < now),
            };
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            term = term.Length > MaxSearchLength ? term[..MaxSearchLength] : term;

            // "#42" or "42" finds ticket 42; any text is looked up in the subject and the description.
            var isNumber = int.TryParse(term.TrimStart('#'), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number);
            tickets = tickets.Where(t => t.Subject.Contains(term) || t.Description.Contains(term) || (isNumber && t.Id == number));
        }

        var total = await tickets.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        var page = Math.Clamp(filter.Page, 1, pageCount);

        var rows = await (
            from t in tickets
            join c in db.Users on t.CustomerId equals c.Id
            join a in db.Users on t.AssigneeId equals a.Id into assignees
            from a in assignees.DefaultIfEmpty()
            orderby t.UpdatedAt descending, t.Id descending
            select new { t.Id, t.Subject, t.Status, t.Priority, CustomerName = c.DisplayName, AssigneeName = a.DisplayName, t.DueAt })
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(ct);

        var items = rows
            .Select(r => new TicketListItem(
                r.Id, r.Subject, r.Status, r.Priority, r.CustomerName, r.AssigneeName, r.DueAt,
                IsOverdue: r.Status != TicketStatus.Closed && now > r.DueAt))
            .ToList();

        return new PagedList<TicketListItem>(items, total, page, PageSize);
    }

    public async Task<TicketDetails?> GetAsync(int id, Actor actor, CancellationToken ct = default)
    {
        var ticket = await db.Tickets
            .AsNoTracking()
            .Include(t => t.Comments)
            .Include(t => t.Attachments)
            .Include(t => t.History)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        // A ticket the user may not see looks the same as a ticket that does not exist.
        if (ticket is null || !ticket.CanBeViewedBy(actor))
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
    public async Task<int> CreateAsync(Actor actor, string subject, string description, TicketPriority priority)
    {
        var ticket = Ticket.Create(subject, description, priority, actor, time.GetUtcNow());
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket.Id;
    }

    public Task<ServiceResult> ChangeStatusAsync(int id, Actor actor, string? rowVersion, TicketStatus status)
        => UpdateAsync(id, actor, rowVersion, (ticket, now) => ticket.ChangeStatus(actor, status, now));

    public Task<ServiceResult> ChangePriorityAsync(int id, Actor actor, string? rowVersion, TicketPriority priority)
        => UpdateAsync(id, actor, rowVersion, (ticket, now) => ticket.ChangePriority(actor, priority, now));

    public Task<ServiceResult> AddCommentAsync(int id, Actor actor, string? rowVersion, string body, bool isInternal)
        => UpdateAsync(id, actor, rowVersion, (ticket, now) => ticket.AddComment(actor, body, isInternal, now));

    /// <summary>
    /// Attaches a file: the rules are checked first, then the content is stored, then the ticket is saved.
    /// When the save fails the stored content is removed again, so storage never keeps files no ticket knows about.
    /// </summary>
    public async Task<ServiceResult> AddAttachmentAsync(
        int id, Actor actor, string? rowVersion, string? fileName, long length, Stream content, CancellationToken ct = default)
    {
        string? storedName = null;

        var result = await UpdateAsync(id, actor, rowVersion, async (ticket, now) =>
        {
            var name = Guid.NewGuid().ToString("N");
            var attachment = ticket.AddAttachment(actor, fileName, length, name, now);
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
    public async Task<AttachmentDownload?> OpenAttachmentAsync(int ticketId, int attachmentId, Actor actor, CancellationToken ct = default)
    {
        var ticket = await db.Tickets
            .AsNoTracking()
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct);

        var attachment = ticket is not null && ticket.CanBeViewedBy(actor)
            ? ticket.Attachments.FirstOrDefault(a => a.Id == attachmentId)
            : null;

        if (attachment is null)
        {
            return null;
        }

        var stream = await attachments.OpenReadAsync(attachment.StoredName, ct);
        return stream is null ? null : new AttachmentDownload(attachment.FileName, attachment.ContentType, stream);
    }

    public async Task<ServiceResult> AssignAsync(int id, Actor actor, string? rowVersion, string? assigneeId)
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
        return await UpdateAsync(id, actor, rowVersion, (ticket, now) => ticket.Assign(actor, target, now));
    }

    private Task<ServiceResult> UpdateAsync(int id, Actor actor, string? rowVersion, Action<Ticket, DateTimeOffset> change)
        => UpdateAsync(id, actor, rowVersion, (ticket, now) =>
        {
            change(ticket, now);
            return Task.CompletedTask;
        });

    private async Task<ServiceResult> UpdateAsync(int id, Actor actor, string? rowVersion, Func<Ticket, DateTimeOffset, Task> change)
    {
        var ticket = await db.Tickets.Include(t => t.Attachments).FirstOrDefaultAsync(t => t.Id == id);

        if (ticket is null || !ticket.CanBeViewedBy(actor))
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

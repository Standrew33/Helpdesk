using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Tickets;

public class IndexModel(TicketService tickets) : PageModel
{
    [BindProperty(Name = "q", SupportsGet = true)]
    public string? Search { get; set; }

    // Each filter can be given several times (?status=New&status=Closed) and then means "any of these".
    [BindProperty(SupportsGet = true)]
    public List<TicketStatus> Status { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public List<TicketPriority> Priority { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public List<TicketScope> Scope { get; set; } = [];

    public static readonly IReadOnlyList<(TicketSort Column, string Title)> Columns =
    [
        (TicketSort.Number, "#"),
        (TicketSort.Subject, "Subject"),
        (TicketSort.Status, "Status"),
        (TicketSort.Priority, "Priority"),
        (TicketSort.Customer, "Customer"),
        (TicketSort.Assignee, "Assignee"),
        (TicketSort.Due, "Due"),
    ];

    [BindProperty(SupportsGet = true)]
    public TicketSort? Sort { get; set; }

    /// <summary>"asc" or "desc"; anything else means the column's natural direction.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    // "page" is taken by Razor Pages routing, so the page number travels as "p".
    [BindProperty(Name = "p", SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public PagedList<TicketListItem> Result { get; private set; } = new([], 0, 1, TicketService.PageSize);

    public bool IsCustomer { get; private set; }

    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || Status.Count > 0 || Priority.Count > 0 || Scope.Count > 0;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        // Every sign-in lands here; the owner of the platform, who belongs to no help desk, goes on to the platform.
        if (User.WorkspaceId() is null)
        {
            return User.IsPlatformOwner() ? RedirectToPage("/Platform/Index") : Forbid();
        }

        var actor = User.ToActor();
        IsCustomer = actor.Role == UserRole.Customer;
        Result = await tickets.ListAsync(actor, new TicketFilter(Search, Status, Priority, Scope, PageNumber, Sort ?? TicketSort.Updated, IsDescending), ct);
        return Page();
    }

    public FilterChoice StatusChoice => new("status", "Status", Enum.GetValues<TicketStatus>()
        .Select(s => new FilterOption(s.ToString(), TicketDisplay.StatusText(s), TicketDisplay.StatusClass(s), Status.Contains(s)))
        .ToList());

    public FilterChoice PriorityChoice => new("priority", "Priority", TicketDisplay.PrioritiesFromHighest
        .Select(p => new FilterOption(p.ToString(), p.ToString(), TicketDisplay.PriorityClass(p), Priority.Contains(p)))
        .ToList());

    public FilterChoice ScopeChoice => new("scope", "Queue",
    [
        new(nameof(TicketScope.Mine), "Assigned to me", "choice-text", Scope.Contains(TicketScope.Mine)),
        new(nameof(TicketScope.Unassigned), "Unassigned", "choice-text", Scope.Contains(TicketScope.Unassigned)),
        new(nameof(TicketScope.Overdue), "Overdue", "choice-text overdue", Scope.Contains(TicketScope.Overdue)),
    ]);

    /// <summary>Descending unless asked otherwise: newest, most urgent and highest number come first.</summary>
    public bool IsDescending => Dir switch
    {
        "asc" => false,
        "desc" => true,
        _ => Sort is null or TicketSort.Updated or TicketSort.Number or TicketSort.Priority,
    };

    /// <summary>"ascending", "descending" or null, for the column header's aria-sort.</summary>
    public string? SortStateOf(TicketSort column)
        => Sort == column ? (IsDescending ? "descending" : "ascending") : null;

    /// <summary>A click on a header sorts by it; a second click turns the order around.</summary>
    public string UrlForSort(TicketSort column)
        => UrlFor(1, column, Sort == column ? (IsDescending ? "asc" : "desc") : null);

    /// <summary>The address of this list with the current filters and sorting and another page number.</summary>
    public string UrlForPage(int page) => UrlFor(page, Sort, Dir);

    private string UrlFor(int page, TicketSort? sort, string? dir)
    {
        var query = new QueryBuilder();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            query.Add("q", Search);
        }

        query.Add("status", Status.Distinct().Select(s => s.ToString()).ToArray());
        query.Add("priority", Priority.Distinct().Select(p => p.ToString()).ToArray());
        query.Add("scope", Scope.Distinct().Select(s => s.ToString()).ToArray());

        if (sort is not null)
        {
            query.Add("sort", sort.Value.ToString());
        }

        if (dir is "asc" or "desc")
        {
            query.Add("dir", dir);
        }

        if (page > 1)
        {
            query.Add("p", page.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return Url.Page("Index") + query.ToQueryString();
    }
}

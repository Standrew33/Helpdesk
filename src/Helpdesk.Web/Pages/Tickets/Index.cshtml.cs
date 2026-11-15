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

    // "page" is taken by Razor Pages routing, so the page number travels as "p".
    [BindProperty(Name = "p", SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public PagedList<TicketListItem> Result { get; private set; } = new([], 0, 1, TicketService.PageSize);

    public bool IsCustomer { get; private set; }

    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || Status.Count > 0 || Priority.Count > 0 || Scope.Count > 0;

    public async Task OnGetAsync(CancellationToken ct)
    {
        var actor = User.ToActor();
        IsCustomer = actor.Role == UserRole.Customer;
        Result = await tickets.ListAsync(actor, new TicketFilter(Search, Status, Priority, Scope, PageNumber), ct);
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

    /// <summary>The address of this list with the current filters and another page number.</summary>
    public string UrlForPage(int page)
    {
        var query = new QueryBuilder();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            query.Add("q", Search);
        }

        query.Add("status", Status.Distinct().Select(s => s.ToString()).ToArray());
        query.Add("priority", Priority.Distinct().Select(p => p.ToString()).ToArray());
        query.Add("scope", Scope.Distinct().Select(s => s.ToString()).ToArray());

        if (page > 1)
        {
            query.Add("p", page.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return Url.Page("Index") + query.ToQueryString();
    }
}

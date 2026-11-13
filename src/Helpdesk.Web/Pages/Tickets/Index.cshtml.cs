using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Tickets;

public class IndexModel(TicketService tickets) : PageModel
{
    [BindProperty(Name = "q", SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public TicketStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public TicketPriority? Priority { get; set; }

    [BindProperty(SupportsGet = true)]
    public TicketScope? Scope { get; set; }

    // "page" is taken by Razor Pages routing, so the page number travels as "p".
    [BindProperty(Name = "p", SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public PagedList<TicketListItem> Result { get; private set; } = new([], 0, 1, TicketService.PageSize);

    public bool IsCustomer { get; private set; }

    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || Status is not null || Priority is not null || Scope is not null;

    public async Task OnGetAsync(CancellationToken ct)
    {
        var actor = User.ToActor();
        IsCustomer = actor.Role == UserRole.Customer;
        Result = await tickets.ListAsync(actor, new TicketFilter(Search, Status, Priority, Scope, PageNumber), ct);
    }

    /// <summary>The current filters with another page number, for the previous/next links.</summary>
    public Dictionary<string, string?> RouteForPage(int page) => new()
    {
        ["q"] = string.IsNullOrWhiteSpace(Search) ? null : Search,
        ["status"] = Status?.ToString(),
        ["priority"] = Priority?.ToString(),
        ["scope"] = Scope?.ToString(),
        ["p"] = page > 1 ? page.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
    };
}

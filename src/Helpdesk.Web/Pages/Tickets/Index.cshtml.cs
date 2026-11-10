using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Tickets;

public class IndexModel(TicketService tickets) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public TicketStatus? Status { get; set; }

    public IReadOnlyList<TicketListItem> Items { get; private set; } = [];

    public bool IsCustomer { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var actor = User.ToActor();
        IsCustomer = actor.Role == UserRole.Customer;
        Items = await tickets.ListAsync(actor, Status, ct);
    }
}

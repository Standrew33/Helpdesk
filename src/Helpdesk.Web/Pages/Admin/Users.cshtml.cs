using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Features.Users;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Admin;

public class UsersModel(UserAdminService admin) : PageModel
{
    [BindProperty(Name = "q", SupportsGet = true)]
    public string? Search { get; set; }

    [TempData]
    public string? Error { get; set; }

    [TempData]
    public string? Notice { get; set; }

    public IReadOnlyList<UserRow> Users { get; private set; } = [];

    public IReadOnlyList<string> Roles => RoleNames.All;

    public int MaxListSize => UserAdminService.MaxListSize;

    public async Task OnGetAsync(CancellationToken ct)
        => Users = await admin.ListAsync(User.ToActor(), Search, ct);

    public Task<IActionResult> OnPostRoleAsync(string id, string role)
        => RunAsync(() => admin.ChangeRoleAsync(User.ToActor(), id, role), "Role updated.");

    public Task<IActionResult> OnPostActiveAsync(string id, bool active)
        => RunAsync(() => admin.SetActiveAsync(User.ToActor(), id, active), active ? "Account activated." : "Account deactivated.");

    private async Task<IActionResult> RunAsync(Func<Task<ServiceResult>> operation, string success)
    {
        var result = await operation();

        if (result.Outcome == ServiceOutcome.NotFound)
        {
            return NotFound();
        }

        if (result.Succeeded)
        {
            Notice = success;
        }
        else
        {
            Error = result.Message;
        }

        return RedirectToPage(new { q = Search });
    }
}

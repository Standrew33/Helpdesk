using System.ComponentModel.DataAnnotations;
using Helpdesk.Core.Tickets;
using Helpdesk.Core.Users;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Tickets;

/// <summary>Subject, description and priority of an open ticket, for the customer who opened it and for staff.</summary>
public class EditModel(TicketService tickets) : PageModel
{
    [FromRoute]
    public int Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>The version of the ticket the form was made from; a newer change by someone else is not overwritten.</summary>
    [BindProperty]
    public string? RowVersion { get; set; }

    [TempData]
    public string? Error { get; set; }

    public IReadOnlyList<TicketPriority> PriorityOptions { get; private set; } = [];

    public PriorityChoice PriorityChoice => new("Input.Priority", "Priority", PriorityOptions, Input.Priority);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var ticket = await LoadAsync(ct);

        if (ticket is null)
        {
            return NotFound();
        }

        if (ticket.Ticket.Status == TicketStatus.Closed)
        {
            Error = "The ticket is closed. Reopen it first.";
            return RedirectToPage("Details", new { id = Id });
        }

        Input = new InputModel
        {
            Subject = ticket.Ticket.Subject,
            Description = ticket.Ticket.Description,
            Priority = ticket.Ticket.Priority,
        };
        RowVersion = ticket.RowVersion;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await tickets.EditAsync(Id, User.ToActor(), RowVersion, Input.Subject, Input.Description, Input.Priority);

            if (result.Succeeded)
            {
                return RedirectToPage("Details", new { id = Id });
            }

            if (result.Outcome == ServiceOutcome.NotFound)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, result.Message ?? "The ticket could not be saved.");
        }

        return await LoadAsync(ct) is null ? NotFound() : Page();
    }

    /// <summary>Loads the ticket the user may see and the priorities they may choose for it.</summary>
    private async Task<TicketDetails?> LoadAsync(CancellationToken ct)
    {
        var actor = User.ToActor();
        var ticket = await tickets.GetAsync(Id, actor, ct);

        if (ticket is not null)
        {
            PriorityOptions = TicketDisplay.PrioritiesFor(actor.Role.IsStaff(), ticket.Ticket.Priority);
        }

        return ticket;
    }

    public class InputModel
    {
        [Required]
        [StringLength(Ticket.MaxSubjectLength)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        [StringLength(Ticket.MaxDescriptionLength)]
        [DataType(DataType.MultilineText)]
        public string Description { get; set; } = string.Empty;

        public TicketPriority Priority { get; set; } = TicketPriority.Normal;
    }
}

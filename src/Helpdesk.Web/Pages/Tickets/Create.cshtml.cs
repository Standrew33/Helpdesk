using System.ComponentModel.DataAnnotations;
using Helpdesk.Core;
using Helpdesk.Core.Tickets;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Tickets;

[Authorize(Roles = RoleNames.Customer)]
public class CreateModel(TicketService tickets) : PageModel
{
    // Customers cannot open urgent tickets; staff can raise the priority later.
    public static readonly IReadOnlyList<SelectListItem> PriorityOptions =
    [
        new("Low", nameof(TicketPriority.Low)),
        new("Normal", nameof(TicketPriority.Normal)),
        new("High", nameof(TicketPriority.High)),
    ];

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var id = await tickets.CreateAsync(User.ToActor(), Input.Subject, Input.Description, Input.Priority);
            return RedirectToPage("Details", new { id });
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
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

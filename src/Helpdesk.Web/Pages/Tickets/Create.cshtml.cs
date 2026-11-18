using System.ComponentModel.DataAnnotations;
using Helpdesk.Core;
using Helpdesk.Core.Tickets;
using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Helpdesk.Web.Pages.Tickets;

[Authorize(Roles = RoleNames.Customer)]
public class CreateModel(TicketService tickets) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>Today in UTC: the earliest due date that can be chosen.</summary>
    public DateOnly Today { get; } = Ticket.Today(DateTimeOffset.UtcNow);

    public PriorityChoice PriorityChoice => new("Input.Priority", "Priority", TicketDisplay.PrioritiesFor(isStaff: false), Input.Priority);

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
            var number = await tickets.CreateAsync(User.ToActor(), Input.Subject, Input.Description ?? string.Empty, Input.Priority, Input.DueDate);
            return RedirectToPage("Details", new { number });
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

        [StringLength(Ticket.MaxDescriptionLength)]
        [DataType(DataType.MultilineText)]
        public string? Description { get; set; }

        public TicketPriority Priority { get; set; } = TicketPriority.Normal;

        [Display(Name = "Due date")]
        public DateOnly? DueDate { get; set; }
    }
}

using Microsoft.AspNetCore.Identity;

namespace Helpdesk.Web.Data;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>The help desk the user belongs to.</summary>
    public int? WorkspaceId { get; set; }

    /// <summary>
    /// Set when the account was deleted. The row stays so that tickets and comments keep an author, but everything
    /// that identified the person is gone and the account can never be used again.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

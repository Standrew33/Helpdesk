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

    /// <summary>When the account was made; empty for accounts made before this was recorded.</summary>
    public DateTimeOffset? CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the person last signed in, by code, link or password; a renewed cookie does not count.</summary>
    public DateTimeOffset? LastSignInAt { get; set; }

    /// <summary>When the person last closed their last open page; while one is open they are online.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }
}

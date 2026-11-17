namespace Helpdesk.Web.Data;

/// <summary>
/// An invitation to join a workspace with a given role, sent by e-mail. Only a hash of the link is stored, so a copy
/// of the database does not hand out working invitations. Withdrawing an invitation removes it.
/// </summary>
public class Invitation
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public Guid Id { get; set; }

    public int WorkspaceId { get; set; }

    /// <summary>The address in lower case, trimmed.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>One of <see cref="RoleNames.All"/>.</summary>
    public string Role { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Who sent it; empty once that account is gone.</summary>
    public string? InvitedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }
}

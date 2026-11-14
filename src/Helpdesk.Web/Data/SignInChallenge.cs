namespace Helpdesk.Web.Data;

public enum ChallengePurpose
{
    SignIn = 0,
    PasswordReset = 1,
}

/// <summary>
/// A one-time code and link sent to an e-mail address. Only hashes are stored, so a copy of the database
/// does not hand out working codes or links.
/// </summary>
public class SignInChallenge
{
    public Guid Id { get; set; }

    /// <summary>The address in upper case, the way Identity normalises it.</summary>
    public string Email { get; set; } = string.Empty;

    public ChallengePurpose Purpose { get; set; }

    public string CodeHash { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? ConsumedAt { get; set; }

    public int FailedAttempts { get; set; }
}

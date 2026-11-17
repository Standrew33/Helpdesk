using System.Security.Cryptography;

namespace Helpdesk.Core.Workspaces;

/// <summary>
/// One company's help desk: its people and its tickets. Workspaces share the application but never each other's data.
/// </summary>
public sealed class Workspace
{
    public const int MaxNameLength = 100;

    public const int JoinCodeLength = 32;

    // Required by EF Core.
    private Workspace()
    {
        Name = string.Empty;
        JoinCode = string.Empty;
    }

    public int Id { get; private set; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The number given to the newest ticket; tickets are numbered from 1 in every workspace.</summary>
    public int LastTicketNumber { get; private set; }

    /// <summary>
    /// The secret part of the public link through which anyone can sign up as a customer of this help desk.
    /// It cannot be guessed; a new one makes the old link stop working.
    /// </summary>
    public string JoinCode { get; private set; }

    /// <summary>Whether the public link works; when it does not, people join by invitation only.</summary>
    public bool IsJoinOpen { get; private set; }

    public static Workspace Create(string name, DateTimeOffset now)
    {
        var text = name?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            throw new DomainException("The name of the help desk is required.");
        }

        if (text.Length > MaxNameLength)
        {
            throw new DomainException($"The name of the help desk must be at most {MaxNameLength} characters.");
        }

        return new Workspace { Name = text, CreatedAt = now, JoinCode = NewJoinCode(), IsJoinOpen = true };
    }

    public void RenewJoinCode() => JoinCode = NewJoinCode();

    public void SetJoinOpen(bool open) => IsJoinOpen = open;

    private static string NewJoinCode() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(JoinCodeLength / 2));
}

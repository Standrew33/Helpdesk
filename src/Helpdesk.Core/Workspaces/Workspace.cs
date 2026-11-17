namespace Helpdesk.Core.Workspaces;

/// <summary>
/// One company's help desk: its people and its tickets. Workspaces share the application but never each other's data.
/// </summary>
public sealed class Workspace
{
    public const int MaxNameLength = 100;

    // Required by EF Core.
    private Workspace()
    {
        Name = string.Empty;
    }

    public int Id { get; private set; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The number given to the newest ticket; tickets are numbered from 1 in every workspace.</summary>
    public int LastTicketNumber { get; private set; }

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

        return new Workspace { Name = text, CreatedAt = now };
    }
}

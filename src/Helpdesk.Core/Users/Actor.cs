namespace Helpdesk.Core.Users;

/// <summary>
/// The user performing an operation, as seen by the domain.
/// <paramref name="WorkspaceId"/> is the help desk the user belongs to; nothing outside it is theirs to see.
/// </summary>
public sealed record Actor(string Id, UserRole Role, int WorkspaceId);

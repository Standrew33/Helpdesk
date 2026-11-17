namespace Helpdesk.Core.Users;

/// <summary>
/// The user performing an operation, as seen by the domain.
/// <paramref name="WorkspaceId"/> is the help desk the user belongs to; nothing outside it is theirs to see.
/// <paramref name="IsDemo"/> marks the shared public demo accounts: they may only see and change demo data.
/// </summary>
public sealed record Actor(string Id, UserRole Role, int WorkspaceId, bool IsDemo = false);

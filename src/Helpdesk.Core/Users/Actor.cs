namespace Helpdesk.Core.Users;

/// <summary>
/// The user performing an operation, as seen by the domain.
/// <paramref name="IsDemo"/> marks the shared public demo accounts: they may only see and change demo data.
/// </summary>
public sealed record Actor(string Id, UserRole Role, bool IsDemo = false);

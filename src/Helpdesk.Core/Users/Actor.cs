namespace Helpdesk.Core.Users;

/// <summary>The user performing an operation, as seen by the domain.</summary>
public sealed record Actor(string Id, UserRole Role);

namespace Helpdesk.Web.Features.Users;

/// <summary>Whether a person has a page open right now, and otherwise when they last had one.</summary>
public sealed record Seen(bool IsOnline, DateTimeOffset? LastSeenAt);

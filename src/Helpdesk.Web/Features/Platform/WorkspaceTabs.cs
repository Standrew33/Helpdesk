namespace Helpdesk.Web.Features.Platform;

/// <summary>The two views of a help desk the owner of the platform switches between: its people and its tickets.</summary>
public sealed record WorkspaceTabs(int Id, bool OnTickets);

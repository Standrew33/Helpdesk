namespace Helpdesk.Web.Data;

/// <summary>
/// The workspace that the accounts and tickets made before workspaces existed were moved into.
/// The demo users live here as well; everyone who signs up gets a workspace of their own instead.
/// </summary>
public static class SharedWorkspace
{
    public const int Id = 1;

    public const string Name = "Helpdesk";
}

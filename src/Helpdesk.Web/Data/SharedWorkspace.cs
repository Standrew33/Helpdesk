namespace Helpdesk.Web.Data;

/// <summary>
/// The workspace that the accounts and tickets made before workspaces existed were moved into.
/// New accounts join it too, until signing up creates a workspace of its own.
/// </summary>
public static class SharedWorkspace
{
    public const int Id = 1;

    public const string Name = "Helpdesk";
}

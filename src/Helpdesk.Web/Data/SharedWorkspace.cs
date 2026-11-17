namespace Helpdesk.Web.Data;

/// <summary>
/// The workspace that the accounts and tickets made before workspaces existed were moved into.
/// Everyone who signs up now gets a workspace of their own instead.
/// </summary>
public static class SharedWorkspace
{
    public const int Id = 1;

    public const string Name = "Helpdesk";
}

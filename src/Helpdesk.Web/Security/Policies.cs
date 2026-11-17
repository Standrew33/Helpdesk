namespace Helpdesk.Web.Security;

public static class Policies
{
    /// <summary>Operators and admins.</summary>
    public const string Staff = "Staff";

    public const string AdminOnly = "AdminOnly";

    /// <summary>People who belong to a help desk; the owner of the platform may not.</summary>
    public const string Workspace = "Workspace";

    /// <summary>The owner of the platform, the superadmin.</summary>
    public const string Platform = "Platform";
}

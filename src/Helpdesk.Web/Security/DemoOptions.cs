namespace Helpdesk.Web.Security;

public class DemoOptions
{
    public const string SectionName = "Demo";

    /// <summary>Shows one-click sign-in buttons for the demo users. Off by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>Creates roles and demo users at startup. Off by default.</summary>
    public bool SeedUsers { get; set; }
}

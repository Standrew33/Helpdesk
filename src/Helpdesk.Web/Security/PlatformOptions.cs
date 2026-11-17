namespace Helpdesk.Web.Security;

/// <summary>
/// The owner of the whole platform: the superadmin above every help desk. Only configuration decides who it is
/// (for example the App Service setting Platform__OwnerEmail), so nobody can become it or take it away in the app.
/// </summary>
public class PlatformOptions
{
    public const string SectionName = "Platform";

    public string? OwnerEmail { get; set; }

    public bool IsOwner(string? email)
        => !string.IsNullOrWhiteSpace(OwnerEmail)
            && email is not null
            && string.Equals(email.Trim(), OwnerEmail.Trim(), StringComparison.OrdinalIgnoreCase);
}

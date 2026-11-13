namespace Helpdesk.Web.Security;

public class SiteOptions
{
    public const string SectionName = "Site";

    /// <summary>
    /// Content of the Google Search Console "HTML tag" verification. When set, it is written into the head of every page.
    /// It is a public value, not a secret.
    /// </summary>
    public string? GoogleSiteVerification { get; set; }
}

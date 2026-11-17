namespace Helpdesk.Web.Security;

public class SiteOptions
{
    public const string SectionName = "Site";

    /// <summary>
    /// Content of the Google Search Console "HTML tag" verification. When set, it is written into the head of every page.
    /// It is a public value, not a secret.
    /// </summary>
    public string? GoogleSiteVerification { get; set; }

    /// <summary>
    /// The address of the site, used for links in e-mails. Links are never built from the Host header of the request,
    /// which a client can forge to plant its own address in someone else's sign-in e-mail. Required in production.
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>The configured address of the site; the request's own host only when none is configured (development, tests).</summary>
    public string BaseUrl(Microsoft.AspNetCore.Http.HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return !string.IsNullOrWhiteSpace(PublicUrl)
            ? PublicUrl.TrimEnd('/')
            : $"{request.Scheme}://{request.Host}{request.PathBase}";
    }
}

/// <summary>How the application's cookies are protected; decided once at start-up.</summary>
public class AppCookieOptions
{
    /// <summary>"__Host-" outside development: the browser keeps the cookie for this exact host, over HTTPS only.</summary>
    public string Prefix { get; set; } = "__Host-";

    public Microsoft.AspNetCore.Http.CookieSecurePolicy SecurePolicy { get; set; } = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
}

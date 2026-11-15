using Microsoft.Net.Http.Headers;

namespace Helpdesk.Web.Security;

/// <summary>Response headers that tell the browser to keep the application inside its own origin.</summary>
public static class SecurityHeaders
{
    /// <summary>
    /// Everything the pages load comes from the same origin (scripts, styles and fonts are bundled with the app),
    /// so nothing else is allowed: no inline script, no inline style, no plug-ins, no framing, no foreign form targets.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; "
        + "connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    /// <summary>
    /// The policy for the few pages with a protected form, as Google recommends for reCAPTCHA: only scripts that
    /// carry this response's nonce run, and the scripts they add themselves ('strict-dynamic'). reCAPTCHA builds
    /// some of its scripts on the fly, inline ones among them, which a list of hosts cannot allow.
    /// </summary>
    public static string CaptchaContentSecurityPolicy(string nonce)
        => $"default-src 'self'; script-src 'nonce-{nonce}' 'strict-dynamic'; "
        + "style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self' https://www.google.com/recaptcha/; "
        + "frame-src https://www.google.com/recaptcha/ https://recaptcha.google.com/recaptcha/; "
        + "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    /// <summary>For files served back to the user: the browser may save them but never run or render them.</summary>
    public const string DownloadContentSecurityPolicy = "default-src 'none'; sandbox";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;

                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers["Cross-Origin-Resource-Policy"] = "same-origin";

                // Pages and endpoints may set a stricter value of their own (downloads do).
                if (!headers.ContainsKey(HeaderNames.ContentSecurityPolicy))
                {
                    headers.ContentSecurityPolicy = ContentSecurityPolicy;
                }

                // Pages show private data: they must not be kept by shared caches or reopened from the cache after sign-out.
                // Static files carry their own caching headers and keep them.
                if (!headers.ContainsKey(HeaderNames.CacheControl))
                {
                    headers.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });

            await next(context);
        });
    }
}

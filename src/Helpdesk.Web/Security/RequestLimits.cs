using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Helpdesk.Web.Security;

/// <summary>Limits for actions an attacker would repeat many times. They apply per client address.</summary>
public class RequestLimitOptions
{
    public const string SectionName = "RequestLimits";

    /// <summary>Sign-in form submissions (address, code, link, password) per minute. Lock-outs work per account; this works per address.</summary>
    public int SignInAttemptsPerMinute { get; set; } = 10;

    /// <summary>
    /// E-mails with a code per hour. Each one uses up the mail provider's free allowance and lands in a mailbox,
    /// so one address must not be able to send them to many people. Each recipient has its own limit as well.
    /// </summary>
    public int CodesPerHour { get; set; } = 10;

    /// <summary>New accounts per hour.</summary>
    public int RegistrationsPerHour { get; set; } = 10;

    /// <summary>Tickets, comments and status changes per ten minutes.</summary>
    public int TicketChangesPer10Minutes { get; set; } = 60;

    /// <summary>File uploads per hour. Storage is the scarce resource of the free tier.</summary>
    public int UploadsPerHour { get; set; } = 20;
}

/// <summary>The counters behind the limits: one fixed window per kind of action and client address.</summary>
public sealed class RequestLimiter : IDisposable
{
    private static readonly string[] SignInPaths =
    [
        "/Account/Login", "/Account/Password", "/Account/Code", "/Account/Link", "/Account/NewPassword", "/Account/Settings",
    ];

    private readonly PartitionedRateLimiter<HttpContext> _limiter;

    public RequestLimiter(IOptions<RequestLimitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;

        // Reading pages is not limited; only the submissions an attacker would repeat are.
        _limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var bucket = BucketFor(context.Request);

            if (bucket is null)
            {
                return RateLimitPartition.GetNoLimiter("none");
            }

            var (name, permits, window) = bucket.Value switch
            {
                Bucket.Codes => ("codes", limits.CodesPerHour, TimeSpan.FromHours(1)),
                Bucket.SignIn => ("signin", limits.SignInAttemptsPerMinute, TimeSpan.FromMinutes(1)),
                Bucket.Registration => ("register", limits.RegistrationsPerHour, TimeSpan.FromHours(1)),
                Bucket.Upload => ("upload", limits.UploadsPerHour, TimeSpan.FromHours(1)),
                _ => ("tickets", limits.TicketChangesPer10Minutes, TimeSpan.FromMinutes(10)),
            };

            return RateLimitPartition.GetFixedWindowLimiter($"{name}:{ClientKey(context)}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = window,
                QueueLimit = 0,
            });
        });
    }

    /// <summary>Counts the request; null when it is allowed, otherwise how long until it would be.</summary>
    public TimeSpan? Refuse(HttpContext context)
    {
        using var lease = _limiter.AttemptAcquire(context);

        if (lease.IsAcquired)
        {
            return null;
        }

        return lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : TimeSpan.FromMinutes(1);
    }

    public void Dispose() => _limiter.Dispose();

    /// <summary>The client address; an IPv6 address stands for a whole /64 network, which one subscriber controls.</summary>
    public static string ClientKey(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var address = context.Connection.RemoteIpAddress;

        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            return new IPAddress(bytes).ToString();
        }

        return address.ToString();
    }

    private enum Bucket
    {
        Codes,
        SignIn,
        Registration,
        Upload,
        TicketChange,
    }

    private static Bucket? BucketFor(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
        {
            return null;
        }

        var path = request.Path;
        var handler = request.Query["handler"].ToString();

        if (IsCodeRequest(path, handler))
        {
            return Bucket.Codes;
        }

        if (SignInPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            return Bucket.SignIn;
        }

        if (path.Equals("/Account/Welcome", StringComparison.OrdinalIgnoreCase))
        {
            return Bucket.Registration;
        }

        if (path.StartsWithSegments("/Tickets", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(handler, "Attachment", StringComparison.OrdinalIgnoreCase)
                ? Bucket.Upload
                : Bucket.TicketChange;
        }

        return null;
    }

    /// <summary>Requests that make the application send an e-mail with a code.</summary>
    private static bool IsCodeRequest(PathString path, string handler)
        => (path.Equals("/Account/Login", StringComparison.OrdinalIgnoreCase) && handler.Length == 0)
            || (path.Equals("/Account/ForgotPassword", StringComparison.OrdinalIgnoreCase) && handler.Length == 0)
            || (path.Equals("/Account/Code", StringComparison.OrdinalIgnoreCase) && string.Equals(handler, "Resend", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Applies the limits to form posts. A refused post never reaches its handler: the page is shown as if it had just
/// been opened, with what was typed still in the form and the reason as a form error (status 429, with Retry-After).
/// </summary>
public sealed class RequestLimitFilter(RequestLimiter limiter, ILogger<RequestLimitFilter> logger) : IAsyncPageFilter
{
    private static readonly object RefusedKey = new();

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (limiter.Refuse(context.HttpContext) is { } retryAfter)
        {
            context.HttpContext.Items[RefusedKey] = retryAfter;

            // The page's own GET handler prepares everything the page needs, and changes nothing.
            context.HandlerMethod = context.ActionDescriptor.HandlerMethods
                .FirstOrDefault(h => HttpMethods.IsGet(h.HttpMethod) && string.IsNullOrEmpty(h.Name));
        }

        return Task.CompletedTask;
    }

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.HttpContext.Items.TryGetValue(RefusedKey, out var value) && value is TimeSpan retryAfter)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            context.ModelState.AddModelError(string.Empty, Message(seconds));
            SecurityLog.RequestLimited(logger, context.HttpContext.Request.Path, RequestLimiter.ClientKey(context.HttpContext));

            if (context.HandlerMethod is null)
            {
                context.Result = new StatusCodeResult(StatusCodes.Status429TooManyRequests);
                return;
            }
        }

        await next();
    }

    public static string Message(int seconds)
    {
        var wait = seconds < 60
            ? (seconds == 1 ? "1 second" : $"{seconds} seconds")
            : (seconds < 120 ? "1 minute" : $"{(int)Math.Ceiling(seconds / 60.0)} minutes");

        return $"Too many attempts from your network. Try again in {wait}.";
    }
}

public static class RequestLimits
{
    public static IServiceCollection AddRequestLimits(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RequestLimitOptions>(configuration.GetSection(RequestLimitOptions.SectionName));
        services.AddSingleton<RequestLimiter>();
        services.AddScoped<RequestLimitFilter>();
        services.Configure<MvcOptions>(options => options.Filters.AddService<RequestLimitFilter>());
        return services;
    }
}

using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
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

public static class RequestLimits
{
    private static readonly string[] SignInPaths =
    [
        "/Account/Login", "/Account/Password", "/Account/Code", "/Account/Link", "/Account/Settings",
    ];

    public static IServiceCollection AddRequestLimits(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RequestLimitOptions>(configuration.GetSection(RequestLimitOptions.SectionName));
        services.AddRateLimiter(_ => { });

        // Configured through the options system, so the limits are read from the final configuration (tests change them).
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RequestLimitOptions>>((options, configured) =>
        {
            var limits = configured.Value;
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Helpdesk.Security");
                SecurityLog.RequestLimited(logger, context.HttpContext.Request.Path, ClientKey(context.HttpContext));
                return ValueTask.CompletedTask;
            };

            // Reading pages is not limited; only the submissions an attacker would repeat are.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
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
        });

        return services;
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
            || (path.Equals("/Account/Code", StringComparison.OrdinalIgnoreCase) && string.Equals(handler, "Resend", StringComparison.OrdinalIgnoreCase));

    /// <summary>The client address; an IPv6 address stands for a whole /64 network, which one subscriber controls.</summary>
    private static string ClientKey(HttpContext context)
    {
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
}

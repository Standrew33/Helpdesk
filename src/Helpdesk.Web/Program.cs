using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Attachments;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Features.Users;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Do not announce the web server software; the request size is small unless a page asks for more (uploads do).
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 1024 * 1024;
});

builder.Services.AddRazorPages(options => options.Conventions.AuthorizeFolder("/Admin", Policies.AdminOnly));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddAttachmentStore(builder.Configuration);
builder.Services.AddScoped<TicketService>();
builder.Services.AddScoped<UserAdminService>();
builder.Services.AddSingleton<SignInTimingGuard>();
builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.SectionName));
builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection(SiteOptions.SectionName));
builder.Services.AddRequestLimits(builder.Configuration);

// The application runs behind the Azure front end, which is the only proxy in front of it. Its address is what
// the request limits and the logs must see, and its scheme decides whether a request counts as secure.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));

builder.Services.AddDbContext<HelpdeskDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        // Serverless Azure SQL pauses when idle and needs a moment to resume; retry transient failures.
        sql => sql.EnableRetryOnFailure()));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;

        // Length matters more than character mix; see PasswordPolicyValidator for the other rules.
        options.Password.RequiredLength = 12;
        options.Password.RequiredUniqueChars = 6;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<HelpdeskDbContext>()
    .AddDefaultTokenProviders()
    .AddPasswordValidator<PasswordPolicyValidator>()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>();

// Slower password hashing makes stolen hashes costlier to crack (OWASP: 210,000 for PBKDF2 with SHA-512).
// Older hashes are upgraded the next time their owner signs in.
builder.Services.AddOptions<PasswordHasherOptions>().Configure<IConfiguration>((options, configuration) =>
    options.IterationCount = configuration.GetValue("Security:PasswordHashIterations", 210_000));

// Deactivating a user or changing a role must take effect on the next request, not after the default 30 minutes.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);

// Every cookie the application sets is invisible to scripts and sent over HTTPS only. The one exception is local
// development over plain http (the "http" launch profile), where browsers refuse secure and "__Host-" cookies.
var secureCookies = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

// The __Host- prefix makes browsers refuse the cookie unless it is secure, bound to this exact host and to the root path.
var cookiePrefix = builder.Environment.IsDevelopment() ? string.Empty : "__Host-";

builder.Services.ConfigureAll<CookieAuthenticationOptions>(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = secureCookies;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = cookiePrefix + "Helpdesk.Auth";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = cookiePrefix + "Helpdesk.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = secureCookies;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services.Configure<CookieTempDataProviderOptions>(options =>
{
    options.Cookie.Name = cookiePrefix + "Helpdesk.TempData";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = secureCookies;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build())
    .AddPolicy(Policies.Staff, policy => policy.RequireRole(RoleNames.Operator, RoleNames.Admin))
    .AddPolicy(Policies.AdminOnly, policy => policy.RequireRole(RoleNames.Admin));

var app = builder.Build();

var migrate = app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup");

// A paused serverless database can need longer than one connection attempt to wake up,
// so the start-up work is retried a few times before the application gives up.
const int startupAttempts = 6;

for (var attempt = 1; ; attempt++)
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();

        if (migrate)
        {
            await scope.ServiceProvider.GetRequiredService<HelpdeskDbContext>().Database.MigrateAsync();
        }

        await DatabaseSeeder.SeedAsync(scope.ServiceProvider);
        break;
    }
    catch (Exception ex) when (attempt < startupAttempts)
    {
        StartupLog.DatabaseNotReady(app.Logger, ex, attempt, startupAttempts);
        await Task.Delay(TimeSpan.FromSeconds(10));
    }
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseSecurityHeaders();

// Missing pages and refused requests get the same friendly page as errors do.
app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");

app.UseHttpsRedirection();

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

// Makes the entry point visible to the integration tests.
public partial class Program;

using Helpdesk.Web.Data;
using Helpdesk.Web.Features.Attachments;
using Helpdesk.Web.Features.Tickets;
using Helpdesk.Web.Features.Users;
using Helpdesk.Web.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options => options.Conventions.AuthorizeFolder("/Admin", Policies.AdminOnly));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddAttachmentStore(builder.Configuration);
builder.Services.AddScoped<TicketService>();
builder.Services.AddScoped<UserAdminService>();
builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.SectionName));
builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection(SiteOptions.SectionName));

builder.Services.AddDbContext<HelpdeskDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        // Serverless Azure SQL pauses when idle and needs a moment to resume; retry transient failures.
        sql => sql.EnableRetryOnFailure()));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<HelpdeskDbContext>()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>();

// Deactivating a user or changing a role must take effect on the next request, not after the default 30 minutes.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = "Helpdesk.Auth";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Missing pages and refused requests get the same friendly page as errors do.
app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

// Makes the entry point visible to the integration tests.
public partial class Program;

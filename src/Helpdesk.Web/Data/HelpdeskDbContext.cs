using Helpdesk.Core.Tickets;
using Helpdesk.Core.Workspaces;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Data;

public class HelpdeskDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly CurrentWorkspace? _workspace;

    public HelpdeskDbContext(DbContextOptions<HelpdeskDbContext> options, CurrentWorkspace? workspace = null)
        : base(options)
    {
        _workspace = workspace;
    }

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<SignInChallenge> SignInChallenges => Set<SignInChallenge>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<Notification> Notifications => Set<Notification>();

    /// <summary>
    /// The only workspace whose tickets and notifications this context reads. It is read anew for every query.
    /// Without one (start-up work, a signed-out request) none is read at all, unless a query asks for every
    /// workspace on purpose with <c>IgnoreQueryFilters</c>.
    /// </summary>
    public int? WorkspaceId => _workspace?.Id;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(HelpdeskDbContext).Assembly);

        // A second guard behind the checks of the services: a query that forgets the workspace still stays inside it.
        builder.Entity<Ticket>().HasQueryFilter(t => t.WorkspaceId == WorkspaceId);
        builder.Entity<Notification>().HasQueryFilter(n => n.WorkspaceId == WorkspaceId);
    }
}

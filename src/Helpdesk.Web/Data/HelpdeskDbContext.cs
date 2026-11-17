using Helpdesk.Core.Tickets;
using Helpdesk.Core.Workspaces;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Helpdesk.Web.Data;

public class HelpdeskDbContext : IdentityDbContext<ApplicationUser>
{
    public HelpdeskDbContext(DbContextOptions<HelpdeskDbContext> options)
        : base(options)
    {
    }

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<SignInChallenge> SignInChallenges => Set<SignInChallenge>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(HelpdeskDbContext).Assembly);
    }
}

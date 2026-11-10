using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Helpdesk.Web.Data;

/// <summary>Used by the EF Core tools (migrations) only; it never runs in the application.</summary>
public class HelpdeskDbContextFactory : IDesignTimeDbContextFactory<HelpdeskDbContext>
{
    public HelpdeskDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=Helpdesk;Trusted_Connection=True";

        var options = new DbContextOptionsBuilder<HelpdeskDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new HelpdeskDbContext(options);
    }
}

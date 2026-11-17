using Helpdesk.Core.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helpdesk.Web.Data.Configurations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        // A workspace is removed on purpose, together with its people; it never disappears from under them.
        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(u => u.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(u => u.WorkspaceId);
    }
}

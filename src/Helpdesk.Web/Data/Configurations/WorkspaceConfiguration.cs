using Helpdesk.Core.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helpdesk.Web.Data.Configurations;

public class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("Workspaces");

        builder.Property(w => w.Name).HasMaxLength(Workspace.MaxNameLength).IsRequired();
        builder.Property(w => w.JoinCode).HasMaxLength(Workspace.JoinCodeLength).IsUnicode(false).IsRequired();
        builder.HasIndex(w => w.JoinCode).IsUnique();
    }
}

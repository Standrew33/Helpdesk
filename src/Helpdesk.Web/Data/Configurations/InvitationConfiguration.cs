using Helpdesk.Core.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helpdesk.Web.Data.Configurations;

public class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("Invitations");

        builder.Property(i => i.Email).HasMaxLength(256).IsRequired();
        builder.Property(i => i.Role).HasMaxLength(32).IsRequired();
        builder.Property(i => i.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(i => i.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(i => i.InvitedById)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => new { i.WorkspaceId, i.Email });
        builder.HasIndex(i => new { i.WorkspaceId, i.CreatedAt });
    }
}

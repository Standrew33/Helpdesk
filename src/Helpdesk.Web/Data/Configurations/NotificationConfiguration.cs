using Helpdesk.Core.Tickets;
using Helpdesk.Core.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helpdesk.Web.Data.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.Property(n => n.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(n => n.Detail).HasMaxLength(32);

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(n => n.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        // A ticket takes its notifications along when it goes, as when a whole workspace is deleted.
        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(n => n.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(n => n.RecipientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(n => n.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        // The bell asks for the unread ones of a person, newest first.
        builder.HasIndex(n => new { n.RecipientId, n.ReadAt, n.CreatedAt });
        builder.HasIndex(n => n.TicketId);
    }
}

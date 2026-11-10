using Helpdesk.Core.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helpdesk.Web.Data.Configurations;

public class TicketHistoryEntryConfiguration : IEntityTypeConfiguration<TicketHistoryEntry>
{
    public void Configure(EntityTypeBuilder<TicketHistoryEntry> builder)
    {
        builder.ToTable("TicketHistory");

        builder.Property(h => h.Field).HasMaxLength(64).IsRequired();
        builder.Property(h => h.OldValue).HasMaxLength(450);
        builder.Property(h => h.NewValue).HasMaxLength(450);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(h => h.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => h.TicketId);
    }
}

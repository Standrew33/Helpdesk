using Helpdesk.Core.Tickets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helpdesk.Web.Data.Configurations;

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments");

        builder.Property(a => a.FileName).HasMaxLength(AttachmentPolicy.MaxFileNameLength).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.StoredName).HasMaxLength(64).IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.UploadedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.TicketId);
    }
}

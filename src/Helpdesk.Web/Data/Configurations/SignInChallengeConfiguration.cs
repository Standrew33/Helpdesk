using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helpdesk.Web.Data.Configurations;

public class SignInChallengeConfiguration : IEntityTypeConfiguration<SignInChallenge>
{
    public void Configure(EntityTypeBuilder<SignInChallenge> builder)
    {
        builder.ToTable("SignInChallenges");

        builder.Property(c => c.Email).HasMaxLength(256).IsRequired();
        builder.Property(c => c.CodeHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(c => c.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();

        builder.HasIndex(c => c.TokenHash).IsUnique();
        builder.HasIndex(c => new { c.Email, c.Purpose, c.CreatedAt });
    }
}

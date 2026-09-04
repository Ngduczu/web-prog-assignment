using LMS.Api.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Users;

public class ExternalUserConfiguration : IEntityTypeConfiguration<ExternalUser>
{
    public void Configure(EntityTypeBuilder<ExternalUser> builder)
    {
        builder.ToTable("external_users");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(x => x.ExternalUserId)
            .HasColumnName("external_user_id")
            .HasMaxLength(255)
            .IsRequired();

        builder.HasIndex(x => new { x.Provider, x.ExternalUserId })
            .IsUnique();
        builder.HasIndex(x => new { x.UserId, x.Provider })
            .IsUnique();

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

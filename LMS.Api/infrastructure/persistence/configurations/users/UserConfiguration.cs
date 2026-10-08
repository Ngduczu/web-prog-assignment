using LMS.Api.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Users;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id");
        
        builder.Property(x => x.FullName)
            .HasColumnName("full_name")
            .IsRequired()
            .HasMaxLength(100);
        
        builder.Property(x => x.Email)
            .HasColumnName("email")
            .IsRequired()
            .HasMaxLength(255);

        builder.HasIndex(x => x.Email)
            .IsUnique();

        builder.Property(x => x.Phone)
            .HasColumnName("phone")
            .IsRequired(false)
            .HasMaxLength(20);

        builder.Property(x => x.PasswordHash)
            .HasColumnName("password_hash")
            .IsRequired(false);

        builder.Property(x => x.AuthVersion)
            .HasColumnName("auth_version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .IsRequired();
    
        builder.Property(x => x.Role)
            .HasConversion<string>()
            .HasColumnName("role")
            .IsRequired()
            .HasMaxLength(50);
        
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasColumnName("status")
            .IsRequired()
            .HasMaxLength(50);
        
        builder.Property(x => x.AvatarUrl)
            .HasColumnName("avatar_url")
            .IsRequired(false)
            .HasMaxLength(500);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.ModifiedAt)
            .HasColumnName("modified_at")
            .IsRequired();

        builder.Property(x => x.CreatedById)
            .HasColumnName("created_by_id");

        builder.Property(x => x.ModifiedById)
            .HasColumnName("modified_by_id");
        
        builder.HasOne(x => x.CreatedBy)
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
        
        builder.HasOne(x => x.ModifiedBy)
            .WithMany()
            .HasForeignKey(x => x.ModifiedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

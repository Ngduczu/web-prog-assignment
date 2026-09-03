using LMS.Api.domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations;

public class UsersConfiguration : IEntityTypeConfiguration<Users>
{
    public void Configure(EntityTypeBuilder<Users> builder)
    {
        builder.ToTable("users");

        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.Fullname)
            .HasColumnName("fullname")
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

        builder.Property(x => x.CreatedOn)
            .HasColumnName("created_on")
            .IsRequired();

        builder.Property(x => x.ModifiedOn)
            .HasColumnName("modified_on")
            .IsRequired();
    }
}
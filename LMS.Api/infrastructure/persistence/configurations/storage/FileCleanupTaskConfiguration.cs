using LMS.Api.domain.Entities.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Storage;

public sealed class FileCleanupTaskConfiguration : IEntityTypeConfiguration<FileCleanupTask>
{
    public void Configure(EntityTypeBuilder<FileCleanupTask> builder)
    {
        builder.ToTable("file_cleanup_tasks");
        builder.HasKey(task => task.Id);
        builder.Property(task => task.Id).HasColumnName("id");
        builder.Property(task => task.ObjectKey).HasColumnName("object_key").HasMaxLength(1024).IsRequired();
        builder.Property(task => task.AttemptCount).HasColumnName("attempt_count");
        builder.Property(task => task.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(task => task.LastError).HasColumnName("last_error").HasMaxLength(2000);
        builder.Property(task => task.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(task => task.ObjectKey).IsUnique();
        builder.HasIndex(task => task.NextAttemptAt);
    }
}

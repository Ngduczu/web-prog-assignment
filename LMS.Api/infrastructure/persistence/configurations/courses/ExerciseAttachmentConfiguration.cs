using LMS.Api.domain.Entities.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Courses;

public class ExerciseAttachmentConfiguration : IEntityTypeConfiguration<ExerciseAttachment>
{
    public void Configure(EntityTypeBuilder<ExerciseAttachment> builder)
    {
        builder.ToTable("exercise_attachments", table =>
            table.HasCheckConstraint("ck_exercise_attachments_file_size", "file_size > 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ExerciseId).HasColumnName("exercise_id");
        builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.ObjectKey).HasColumnName("object_key").HasMaxLength(1024).IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(255).IsRequired();
        builder.Property(x => x.FileSize).HasColumnName("file_size");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ModifiedAt).HasColumnName("modified_at");
        builder.Property(x => x.CreatedById).HasColumnName("created_by_id");

        builder.HasIndex(x => x.ExerciseId).IsUnique();
        builder.HasIndex(x => x.ObjectKey).IsUnique();

        builder.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
    }
}

using LMS.Api.domain.Entities.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Courses;

public class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("materials", table =>
            table.HasCheckConstraint("ck_materials_file_size", "file_size > 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.LessonId).HasColumnName("lesson_id");
        builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.ObjectKey).HasColumnName("object_key").HasMaxLength(1024).IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(255).IsRequired();
        builder.Property(x => x.FileSize).HasColumnName("file_size");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ModifiedAt).HasColumnName("modified_at");
        builder.Property(x => x.CreatedById).HasColumnName("created_by_id");
        builder.Property(x => x.ModifiedById).HasColumnName("modified_by_id");

        builder.HasIndex(x => x.ObjectKey).IsUnique();

        builder.HasOne(x => x.Lesson).WithMany(x => x.Materials).HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ModifiedBy).WithMany().HasForeignKey(x => x.ModifiedById).OnDelete(DeleteBehavior.Restrict);
    }
}

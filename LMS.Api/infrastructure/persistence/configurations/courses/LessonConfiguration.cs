using LMS.Api.domain.Entities.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Courses;

public class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> builder)
    {
        builder.ToTable("lessons", table =>
            table.HasCheckConstraint("ck_lessons_order", "lesson_order >= 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CourseId).HasColumnName("course_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Content).HasColumnName("content");
        builder.Property(x => x.Order).HasColumnName("lesson_order");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ModifiedAt).HasColumnName("modified_at");
        builder.Property(x => x.CreatedById).HasColumnName("created_by_id");
        builder.Property(x => x.ModifiedById).HasColumnName("modified_by_id");

        builder.HasIndex(x => new { x.CourseId, x.Order }).IsUnique();

        builder.HasOne(x => x.Course).WithMany(x => x.Lessons).HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ModifiedBy).WithMany().HasForeignKey(x => x.ModifiedById).OnDelete(DeleteBehavior.Restrict);
    }
}

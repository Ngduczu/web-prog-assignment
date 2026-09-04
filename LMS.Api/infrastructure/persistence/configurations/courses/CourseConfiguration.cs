using LMS.Api.domain.Entities.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Courses;

public class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable("courses", table =>
            table.HasCheckConstraint("ck_courses_max_students", "max_students > 0"));

        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.IsDeleted);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
        builder.Property(x => x.TeacherId).HasColumnName("teacher_id");
        builder.Property(x => x.MaxStudents).HasColumnName("max_students");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ModifiedAt).HasColumnName("modified_at");
        builder.Property(x => x.CreatedById).HasColumnName("created_by_id");
        builder.Property(x => x.ModifiedById).HasColumnName("modified_by_id");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedById).HasColumnName("deleted_by_id");

        builder.HasIndex(x => x.TeacherId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.DeletedAt);

        builder.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ModifiedBy).WithMany().HasForeignKey(x => x.ModifiedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DeletedBy).WithMany().HasForeignKey(x => x.DeletedById).OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}

using LMS.Api.domain.Entities.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Courses;

public class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.ToTable("submissions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ExerciseId).HasColumnName("exercise_id");
        builder.Property(x => x.StudentId).HasColumnName("student_id");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(x => x.Grade).HasColumnName("grade").HasPrecision(5, 2);
        builder.Property(x => x.Feedback).HasColumnName("feedback").HasMaxLength(4000);
        builder.Property(x => x.GradedAt).HasColumnName("graded_at");
        builder.Property(x => x.GradedById).HasColumnName("graded_by_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ModifiedAt).HasColumnName("modified_at");

        builder.HasIndex(x => new { x.ExerciseId, x.StudentId }).IsUnique();

        builder.HasOne(x => x.Exercise).WithMany().HasForeignKey(x => x.ExerciseId).IsRequired(false).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.GradedBy).WithMany().HasForeignKey(x => x.GradedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Attachment).WithOne(x => x.Submission).HasForeignKey<SubmissionAttachment>(x => x.SubmissionId).OnDelete(DeleteBehavior.Cascade);
    }
}

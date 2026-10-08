using LMS.Api.domain.Entities.Examinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Examinations;

public class ExaminationAttemptConfiguration : IEntityTypeConfiguration<ExaminationAttempt>
{
    public void Configure(EntityTypeBuilder<ExaminationAttempt> builder)
    {
        builder.ToTable("examination_attempts", table =>
        {
            table.HasCheckConstraint("ck_examination_attempts_remaining_seconds", "remaining_seconds IS NULL OR remaining_seconds >= 0");
            table.HasCheckConstraint("ck_examination_attempts_result", "correct_answers >= 0 AND total_questions >= 0 AND score >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ExaminationId).HasColumnName("examination_id");
        builder.Property(x => x.StudentId).HasColumnName("student_id");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.SubmitReason).HasColumnName("submit_reason").HasConversion<string>().HasMaxLength(50);
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(x => x.DisconnectedAt).HasColumnName("disconnected_at");
        builder.Property(x => x.RemainingSeconds).HasColumnName("remaining_seconds");
        builder.Property(x => x.LastActivityAt).HasColumnName("last_activity_at");
        builder.Property(x => x.ViolationCount).HasColumnName("violation_count");
        builder.Property(x => x.CorrectAnswers).HasColumnName("correct_answers");
        builder.Property(x => x.TotalQuestions).HasColumnName("total_questions");
        builder.Property(x => x.Score).HasColumnName("score").HasPrecision(5, 2);
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasIndex(x => new { x.ExaminationId, x.StudentId }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.LastActivityAt });

        builder.HasOne(x => x.Examination).WithMany(x => x.Attempts).HasForeignKey(x => x.ExaminationId).IsRequired(false).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
    }
}

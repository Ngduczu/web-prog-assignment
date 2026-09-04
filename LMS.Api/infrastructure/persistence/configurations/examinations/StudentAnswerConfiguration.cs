using LMS.Api.domain.Entities.Examinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Examinations;

public class StudentAnswerConfiguration : IEntityTypeConfiguration<StudentAnswer>
{
    public void Configure(EntityTypeBuilder<StudentAnswer> builder)
    {
        builder.ToTable("student_answers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ExaminationAttemptId).HasColumnName("examination_attempt_id");
        builder.Property(x => x.ExaminationQuestionId).HasColumnName("examination_question_id");
        builder.Property(x => x.SelectedOptionId).HasColumnName("selected_option_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ModifiedAt).HasColumnName("modified_at");

        builder.HasIndex(x => new { x.ExaminationAttemptId, x.ExaminationQuestionId }).IsUnique();

        builder.HasOne(x => x.ExaminationAttempt).WithMany(x => x.Answers).HasForeignKey(x => x.ExaminationAttemptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ExaminationQuestion).WithMany().HasForeignKey(x => x.ExaminationQuestionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SelectedOption).WithMany().HasForeignKey(x => x.SelectedOptionId).OnDelete(DeleteBehavior.SetNull);
    }
}

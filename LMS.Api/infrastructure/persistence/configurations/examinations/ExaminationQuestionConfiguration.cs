using LMS.Api.domain.Entities.Examinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Examinations;

public class ExaminationQuestionConfiguration : IEntityTypeConfiguration<ExaminationQuestion>
{
    public void Configure(EntityTypeBuilder<ExaminationQuestion> builder)
    {
        builder.ToTable("examination_questions", table =>
            table.HasCheckConstraint("ck_examination_questions_order", "question_order >= 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ExaminationId).HasColumnName("examination_id");
        builder.Property(x => x.SourceQuestionId).HasColumnName("source_question_id");
        builder.Property(x => x.Content).HasColumnName("content").IsRequired();
        builder.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.Difficulty).HasColumnName("difficulty").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.ShuffleOptions).HasColumnName("shuffle_options");
        builder.Property(x => x.Order).HasColumnName("question_order");
        builder.Property(x => x.CorrectOptionId).HasColumnName("correct_option_id");

        builder.HasIndex(x => new { x.ExaminationId, x.Order }).IsUnique();
        builder.HasIndex(x => x.SourceQuestionId);

        builder.HasOne(x => x.Examination).WithMany(x => x.Questions).HasForeignKey(x => x.ExaminationId).IsRequired(false).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CorrectOption).WithMany().HasForeignKey(x => x.CorrectOptionId).OnDelete(DeleteBehavior.SetNull);
    }
}

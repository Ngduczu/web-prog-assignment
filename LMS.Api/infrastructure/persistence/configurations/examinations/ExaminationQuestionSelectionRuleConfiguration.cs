using LMS.Api.domain.Entities.Examinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Examinations;

public class ExaminationQuestionSelectionRuleConfiguration : IEntityTypeConfiguration<ExaminationQuestionSelectionRule>
{
    public void Configure(EntityTypeBuilder<ExaminationQuestionSelectionRule> builder)
    {
        builder.ToTable("examination_question_selection_rules", table =>
            table.HasCheckConstraint("ck_examination_question_selection_rules_count", "question_count > 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ExaminationId).HasColumnName("examination_id");
        builder.Property(x => x.QuestionBankId).HasColumnName("question_bank_id");
        builder.Property(x => x.Difficulty).HasColumnName("difficulty").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.QuestionCount).HasColumnName("question_count");

        builder.HasIndex(x => new { x.ExaminationId, x.QuestionBankId, x.Difficulty }).IsUnique();

        builder.HasOne(x => x.Examination).WithMany(x => x.QuestionSelectionRules).HasForeignKey(x => x.ExaminationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.QuestionBank).WithMany().HasForeignKey(x => x.QuestionBankId).OnDelete(DeleteBehavior.Restrict);
    }
}

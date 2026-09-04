using LMS.Api.domain.Entities.Examinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Examinations;

public class ExaminationFillInBlankAnswerKeyConfiguration : IEntityTypeConfiguration<ExaminationFillInBlankAnswerKey>
{
    public void Configure(EntityTypeBuilder<ExaminationFillInBlankAnswerKey> builder)
    {
        builder.ToTable("examination_fill_in_blank_answer_keys", table =>
            table.HasCheckConstraint("ck_examination_fill_in_blank_answer_keys_order", "blank_order >= 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ExaminationQuestionId).HasColumnName("examination_question_id");
        builder.Property(x => x.BlankOrder).HasColumnName("blank_order");
        builder.Property(x => x.ExpectedAnswer).HasColumnName("expected_answer").HasMaxLength(1000).IsRequired();

        builder.HasIndex(x => new { x.ExaminationQuestionId, x.BlankOrder, x.ExpectedAnswer }).IsUnique();

        builder.HasOne(x => x.ExaminationQuestion)
            .WithMany(x => x.FillInBlankAnswerKeys)
            .HasForeignKey(x => x.ExaminationQuestionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using LMS.Api.domain.Entities.Examinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Examinations;

public class ExaminationMultipleChoiceOptionConfiguration : IEntityTypeConfiguration<ExaminationMultipleChoiceOption>
{
    public void Configure(EntityTypeBuilder<ExaminationMultipleChoiceOption> builder)
    {
        builder.ToTable("examination_multiple_choice_options", table =>
            table.HasCheckConstraint("ck_examination_multiple_choice_options_order", "option_order >= 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ExaminationQuestionId).HasColumnName("examination_question_id");
        builder.Property(x => x.Content).HasColumnName("content").IsRequired();
        builder.Property(x => x.Order).HasColumnName("option_order");

        builder.HasIndex(x => new { x.ExaminationQuestionId, x.Order }).IsUnique();

        builder.HasOne(x => x.ExaminationQuestion)
            .WithMany(x => x.MultipleChoiceOptions)
            .HasForeignKey(x => x.ExaminationQuestionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

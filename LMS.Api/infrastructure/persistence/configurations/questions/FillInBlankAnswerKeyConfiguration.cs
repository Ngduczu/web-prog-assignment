using LMS.Api.domain.Entities.Questions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Questions;

public class FillInBlankAnswerKeyConfiguration : IEntityTypeConfiguration<FillInBlankAnswerKey>
{
    public void Configure(EntityTypeBuilder<FillInBlankAnswerKey> builder)
    {
        builder.ToTable("fill_in_blank_answer_keys", table =>
            table.HasCheckConstraint("ck_fill_in_blank_answer_keys_order", "blank_order >= 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.QuestionId).HasColumnName("question_id");
        builder.Property(x => x.BlankOrder).HasColumnName("blank_order");
        builder.Property(x => x.ExpectedAnswer).HasColumnName("expected_answer").HasMaxLength(1000).IsRequired();

        builder.HasIndex(x => new { x.QuestionId, x.BlankOrder, x.ExpectedAnswer }).IsUnique();

        builder.HasOne(x => x.Question)
            .WithMany(x => x.FillInBlankAnswerKeys)
            .HasForeignKey(x => x.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

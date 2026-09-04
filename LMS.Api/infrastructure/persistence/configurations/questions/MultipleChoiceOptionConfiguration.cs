using LMS.Api.domain.Entities.Questions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Questions;

public class MultipleChoiceOptionConfiguration : IEntityTypeConfiguration<MultipleChoiceOption>
{
    public void Configure(EntityTypeBuilder<MultipleChoiceOption> builder)
    {
        builder.ToTable("multiple_choice_options", table =>
            table.HasCheckConstraint("ck_multiple_choice_options_order", "option_order >= 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.QuestionId).HasColumnName("question_id");
        builder.Property(x => x.Content).HasColumnName("content").IsRequired();
        builder.Property(x => x.Order).HasColumnName("option_order");

        builder.HasIndex(x => new { x.QuestionId, x.Order }).IsUnique();

        builder.HasOne(x => x.Question)
            .WithMany(x => x.MultipleChoiceOptions)
            .HasForeignKey(x => x.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

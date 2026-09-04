using LMS.Api.domain.Entities.Questions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Questions;

public class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.ToTable("questions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.QuestionBankId).HasColumnName("question_bank_id");
        builder.Property(x => x.Content).HasColumnName("content").IsRequired();
        builder.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.Difficulty).HasColumnName("difficulty").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.ShuffleOptions).HasColumnName("shuffle_options");
        builder.Property(x => x.CorrectOptionId).HasColumnName("correct_option_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ModifiedAt).HasColumnName("modified_at");
        builder.Property(x => x.CreatedById).HasColumnName("created_by_id");
        builder.Property(x => x.ModifiedById).HasColumnName("modified_by_id");

        builder.HasIndex(x => new { x.QuestionBankId, x.Type, x.Difficulty });

        builder.HasOne(x => x.QuestionBank).WithMany(x => x.Questions).HasForeignKey(x => x.QuestionBankId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CorrectOption).WithMany().HasForeignKey(x => x.CorrectOptionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ModifiedBy).WithMany().HasForeignKey(x => x.ModifiedById).OnDelete(DeleteBehavior.Restrict);
    }
}

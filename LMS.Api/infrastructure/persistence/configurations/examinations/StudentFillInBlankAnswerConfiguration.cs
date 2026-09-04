using LMS.Api.domain.Entities.Examinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Examinations;

public class StudentFillInBlankAnswerConfiguration : IEntityTypeConfiguration<StudentFillInBlankAnswer>
{
    public void Configure(EntityTypeBuilder<StudentFillInBlankAnswer> builder)
    {
        builder.ToTable("student_fill_in_blank_answers", table =>
            table.HasCheckConstraint("ck_student_fill_in_blank_answers_order", "blank_order >= 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.StudentAnswerId).HasColumnName("student_answer_id");
        builder.Property(x => x.BlankOrder).HasColumnName("blank_order");
        builder.Property(x => x.AnswerText).HasColumnName("answer_text").HasMaxLength(1000).IsRequired();

        builder.HasIndex(x => new { x.StudentAnswerId, x.BlankOrder }).IsUnique();

        builder.HasOne(x => x.StudentAnswer)
            .WithMany(x => x.FillInBlankAnswers)
            .HasForeignKey(x => x.StudentAnswerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

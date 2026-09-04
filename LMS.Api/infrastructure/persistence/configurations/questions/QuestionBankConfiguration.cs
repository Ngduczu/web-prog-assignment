using LMS.Api.domain.Entities.Questions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Questions;

public class QuestionBankConfiguration : IEntityTypeConfiguration<QuestionBank>
{
    public void Configure(EntityTypeBuilder<QuestionBank> builder)
    {
        builder.ToTable("question_banks");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TeacherId).HasColumnName("teacher_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ModifiedAt).HasColumnName("modified_at");
        builder.Property(x => x.CreatedById).HasColumnName("created_by_id");
        builder.Property(x => x.ModifiedById).HasColumnName("modified_by_id");

        builder.HasIndex(x => x.TeacherId);

        builder.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ModifiedBy).WithMany().HasForeignKey(x => x.ModifiedById).OnDelete(DeleteBehavior.Restrict);
    }
}

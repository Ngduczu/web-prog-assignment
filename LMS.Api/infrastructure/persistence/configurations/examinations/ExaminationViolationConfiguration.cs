using LMS.Api.domain.Entities.Examinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Examinations;

public class ExaminationViolationConfiguration : IEntityTypeConfiguration<ExaminationViolation>
{
    public void Configure(EntityTypeBuilder<ExaminationViolation> builder)
    {
        builder.ToTable("examination_violations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ExaminationAttemptId).HasColumnName("examination_attempt_id");
        builder.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at");

        builder.HasIndex(x => new { x.ExaminationAttemptId, x.OccurredAt });

        builder.HasOne(x => x.ExaminationAttempt)
            .WithMany(x => x.Violations)
            .HasForeignKey(x => x.ExaminationAttemptId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using LMS.Api.domain.Entities.Examinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Examinations;

public class ExaminationConfiguration : IEntityTypeConfiguration<Examination>
{
    public void Configure(EntityTypeBuilder<Examination> builder)
    {
        builder.ToTable("examinations", table =>
        {
            table.HasCheckConstraint("ck_examinations_schedule", "due_at > start_at");
            table.HasCheckConstraint("ck_examinations_duration", "duration_minutes > 0");
            table.HasCheckConstraint("ck_examinations_anti_cheat", "fullscreen_grace_seconds >= 0 AND max_violations >= 0 AND max_disconnect_minutes >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CourseId).HasColumnName("course_id");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(4000);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.StartAt).HasColumnName("start_at");
        builder.Property(x => x.DueAt).HasColumnName("due_at");
        builder.Property(x => x.DurationMinutes).HasColumnName("duration_minutes");
        builder.Property(x => x.ShuffleQuestions).HasColumnName("shuffle_questions");
        builder.Property(x => x.ShowScoreImmediately).HasColumnName("show_score_immediately");
        builder.Property(x => x.RequireFullscreen).HasColumnName("require_fullscreen");
        builder.Property(x => x.FullscreenGraceSeconds).HasColumnName("fullscreen_grace_seconds");
        builder.Property(x => x.MaxViolations).HasColumnName("max_violations");
        builder.Property(x => x.BlockCopyPaste).HasColumnName("block_copy_paste");
        builder.Property(x => x.BlockRightClick).HasColumnName("block_right_click");
        builder.Property(x => x.DetectTabChange).HasColumnName("detect_tab_change");
        builder.Property(x => x.DetectDevTools).HasColumnName("detect_dev_tools");
        builder.Property(x => x.MaxDisconnectMinutes).HasColumnName("max_disconnect_minutes");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ModifiedAt).HasColumnName("modified_at");
        builder.Property(x => x.CreatedById).HasColumnName("created_by_id");
        builder.Property(x => x.ModifiedById).HasColumnName("modified_by_id");

        builder.HasIndex(x => new { x.CourseId, x.Status });
        builder.HasIndex(x => new { x.StartAt, x.DueAt });

        builder.HasOne(x => x.Course).WithMany(x => x.Examinations).HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ModifiedBy).WithMany().HasForeignKey(x => x.ModifiedById).OnDelete(DeleteBehavior.Restrict);
    }
}

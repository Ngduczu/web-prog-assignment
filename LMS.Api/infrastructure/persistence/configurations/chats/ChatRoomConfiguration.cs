using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Chats;

public class ChatRoomConfiguration : IEntityTypeConfiguration<ChatRoom>
{
    public void Configure(EntityTypeBuilder<ChatRoom> builder)
    {
        builder.ToTable("chat_rooms", table =>
            table.HasCheckConstraint(
                "ck_chat_rooms_type_reference",
                "(type = 'Course' AND course_id IS NOT NULL AND direct_message_key IS NULL) OR " +
                "(type = 'DirectMessage' AND course_id IS NULL AND direct_message_key IS NOT NULL)"));

        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.IsArchived);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(x => x.CourseId).HasColumnName("course_id");
        builder.Property(x => x.DirectMessageKey).HasColumnName("direct_message_key").HasMaxLength(65);
        builder.Property(x => x.CreatedById).HasColumnName("created_by_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ArchivedAt).HasColumnName("archived_at");

        builder.HasIndex(x => x.CourseId).IsUnique();
        builder.HasIndex(x => x.DirectMessageKey).IsUnique();
        builder.HasIndex(x => x.ArchivedAt);

        builder.HasOne<Course>().WithOne().HasForeignKey<ChatRoom>(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Participants)
            .WithOne(x => x.ChatRoom)
            .HasForeignKey(x => x.ChatRoomId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Participants).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

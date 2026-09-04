using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Chats;

public class ChatParticipantConfiguration : IEntityTypeConfiguration<ChatParticipant>
{
    public void Configure(EntityTypeBuilder<ChatParticipant> builder)
    {
        builder.ToTable("chat_participants");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ChatRoomId).HasColumnName("chat_room_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.JoinedAt).HasColumnName("joined_at");
        builder.Property(x => x.LastReadMessageId).HasColumnName("last_read_message_id");

        builder.HasIndex(x => new { x.ChatRoomId, x.UserId }).IsUnique();
        builder.HasIndex(x => x.UserId);

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ChatMessage>().WithMany().HasForeignKey(x => x.LastReadMessageId).OnDelete(DeleteBehavior.SetNull);
    }
}

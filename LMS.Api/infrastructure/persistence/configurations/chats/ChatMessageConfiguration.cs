using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Chats;

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("chat_messages");

        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.IsDeleted);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ChatRoomId).HasColumnName("chat_room_id");
        builder.Property(x => x.SenderId).HasColumnName("sender_id");
        builder.Property(x => x.Content).HasColumnName("content").HasMaxLength(4000);
        builder.Property(x => x.ReplyToMessageId).HasColumnName("reply_to_message_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.EditedAt).HasColumnName("edited_at");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(x => new { x.ChatRoomId, x.CreatedAt, x.Id });
        builder.HasIndex(x => x.SenderId);

        builder.HasOne(x => x.ChatRoom).WithMany().HasForeignKey(x => x.ChatRoomId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ChatMessage>().WithMany().HasForeignKey(x => x.ReplyToMessageId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(x => x.Attachments)
            .WithOne(x => x.ChatMessage)
            .HasForeignKey(x => x.ChatMessageId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

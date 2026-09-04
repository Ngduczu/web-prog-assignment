using LMS.Api.domain.Entities.Chats;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LMS.Api.infrastructure.Persistence.Configurations.Chats;

public class ChatAttachmentConfiguration : IEntityTypeConfiguration<ChatAttachment>
{
    public void Configure(EntityTypeBuilder<ChatAttachment> builder)
    {
        builder.ToTable("chat_attachments", table =>
            table.HasCheckConstraint(
                "ck_chat_attachments_file_size",
                $"file_size > 0 AND file_size < {ChatAttachment.MaximumFileSizeInBytes}"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ChatMessageId).HasColumnName("chat_message_id");
        builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.ObjectKey).HasColumnName("object_key").HasMaxLength(1024).IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(255).IsRequired();
        builder.Property(x => x.FileSize).HasColumnName("file_size");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(x => x.ObjectKey).IsUnique();
    }
}

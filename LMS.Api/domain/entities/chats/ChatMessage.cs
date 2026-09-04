namespace LMS.Api.domain.Entities.Chats;

public class ChatMessage
{
    private readonly List<ChatAttachment> _attachments = new();

    public Guid Id { get; private set; }
    public Guid ChatRoomId { get; private set; }
    public ChatRoom? ChatRoom { get; private set; }
    public Guid SenderId { get; private set; }
    public string? Content { get; private set; }
    public Guid? ReplyToMessageId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? EditedAt { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public bool IsDeleted => DeletedAt.HasValue;
    public IReadOnlyCollection<ChatAttachment> Attachments => _attachments;

    private ChatMessage() { }

    public ChatMessage(Guid chatRoomId, Guid senderId, string? content, Guid? replyToMessageId = null)
    {
        Id = Guid.NewGuid();
        ChatRoomId = chatRoomId;
        SenderId = senderId;
        Content = NormalizeContent(content);
        ReplyToMessageId = replyToMessageId;
        CreatedAt = DateTime.UtcNow;
    }

    public ChatAttachment AddAttachment(string fileName, string objectKey, string contentType, long fileSize)
    {
        EnsureNotDeleted();

        var attachment = new ChatAttachment(Id, fileName, objectKey, contentType, fileSize);
        _attachments.Add(attachment);
        return attachment;
    }

    public void EnsureReadyToSend()
    {
        if (Content is null && _attachments.Count == 0)
        {
            throw new InvalidOperationException("A chat message must contain text or at least one attachment.");
        }
    }

    public void EditContent(string? content)
    {
        EnsureNotDeleted();

        var normalizedContent = NormalizeContent(content);
        if (normalizedContent is null && _attachments.Count == 0)
        {
            throw new InvalidOperationException("A chat message must contain text or at least one attachment.");
        }

        Content = normalizedContent;
        EditedAt = DateTime.UtcNow;
    }

    public void Delete()
    {
        if (IsDeleted)
        {
            return;
        }

        DeletedAt = DateTime.UtcNow;
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A deleted chat message cannot be modified.");
        }
    }

    private static string? NormalizeContent(string? content)
    {
        return string.IsNullOrWhiteSpace(content) ? null : content;
    }
}

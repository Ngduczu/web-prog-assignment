namespace LMS.Api.domain.Entities.Chats;

public class ChatAttachment
{
    public const long MaximumFileSizeInBytes = 20_000_000L;

    public Guid Id { get; private set; }
    public Guid ChatMessageId { get; private set; }
    public ChatMessage? ChatMessage { get; private set; }
    public string FileName { get; private set; } = null!;
    public string ObjectKey { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long FileSize { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private ChatAttachment() { }

    internal ChatAttachment(Guid chatMessageId, string fileName, string objectKey, string contentType, long fileSize)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name is required.", nameof(fileName));
        }

        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("Object key is required.", nameof(objectKey));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("Content type is required.", nameof(contentType));
        }

        if (fileSize <= 0 || fileSize >= MaximumFileSizeInBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSize), "File size must be greater than zero and less than 20 MB.");
        }

        Id = Guid.NewGuid();
        ChatMessageId = chatMessageId;
        FileName = fileName;
        ObjectKey = objectKey;
        ContentType = contentType;
        FileSize = fileSize;
        CreatedAt = DateTime.UtcNow;
    }
}

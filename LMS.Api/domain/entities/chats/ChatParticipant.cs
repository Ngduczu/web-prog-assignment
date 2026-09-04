namespace LMS.Api.domain.Entities.Chats;

public class ChatParticipant
{
    public Guid Id { get; private set; }
    public Guid ChatRoomId { get; private set; }
    public ChatRoom? ChatRoom { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime JoinedAt { get; private set; }
    public Guid? LastReadMessageId { get; private set; }

    private ChatParticipant() { }

    internal ChatParticipant(Guid chatRoomId, Guid userId)
    {
        Id = Guid.NewGuid();
        ChatRoomId = chatRoomId;
        UserId = userId;
        JoinedAt = DateTime.UtcNow;
    }

    public void MarkAsRead(Guid messageId)
    {
        LastReadMessageId = messageId;
    }
}

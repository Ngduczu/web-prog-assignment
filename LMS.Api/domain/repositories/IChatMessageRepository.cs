using LMS.Api.domain.Entities.Chats;

namespace LMS.Api.domain.repositories;

public interface IChatMessageRepository
{
    Task<ChatMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> ListByRoomAsync(Guid roomId, int skip, int take, DateTime? visibleAfter, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, ChatRoomLatestMessage>> ListLatestByRoomAsync(Guid userId, IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken);
    Task<IReadOnlySet<Guid>> ListUnreadRoomIdsAsync(Guid userId, IReadOnlyCollection<Guid> roomIds, CancellationToken cancellationToken);
    void Add(ChatMessage message);
}

public sealed record ChatRoomLatestMessage(Guid ChatRoomId, Guid Id, Guid SenderId, string? Content, DateTime CreatedAt, bool HasAttachments);

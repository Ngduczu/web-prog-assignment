using LMS.Api.domain.Entities.Chats;

namespace LMS.Api.domain.repositories;

public interface IChatMessageRepository
{
    Task<ChatMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> ListByRoomAsync(Guid roomId, int skip, int take, CancellationToken cancellationToken);
    void Add(ChatMessage message);
}
using LMS.Api.domain.Entities.Chats;

namespace LMS.Api.domain.repositories;

public interface IChatRoomRepository
{
    Task<ChatRoom?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<ChatRoom?> GetByCourseIdAsync(Guid courseId, CancellationToken cancellationToken);
    Task<ChatRoom?> GetByDirectMessageKeyAsync(string key, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatRoom>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);
    void Add(ChatRoom room);
}
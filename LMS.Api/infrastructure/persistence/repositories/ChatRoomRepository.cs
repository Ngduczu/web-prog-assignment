using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.repositories;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence.Repositories;

public sealed class ChatRoomRepository : IChatRoomRepository
{
    private readonly AppDbContext _dbContext;
    public ChatRoomRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<ChatRoom?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.ChatRooms.Include(room => room.Participants).SingleOrDefaultAsync(room => room.Id == id, cancellationToken);

    public Task<ChatRoom?> GetByCourseIdAsync(Guid courseId, CancellationToken cancellationToken) =>
        _dbContext.ChatRooms.Include(room => room.Participants).SingleOrDefaultAsync(room => room.CourseId == courseId, cancellationToken);

    public Task<ChatRoom?> GetByDirectMessageKeyAsync(string key, CancellationToken cancellationToken) =>
        _dbContext.ChatRooms.Include(room => room.Participants).SingleOrDefaultAsync(room => room.DirectMessageKey == key, cancellationToken);

    public async Task<IReadOnlyList<ChatRoom>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await _dbContext.ChatRooms
            .Include(room => room.Participants)
            .Where(room => room.Participants.Any(participant => participant.UserId == userId))
            .OrderByDescending(room => room.CreatedAt)
            .ToListAsync(cancellationToken);

    public void Add(ChatRoom room) => _dbContext.ChatRooms.Add(room);
}
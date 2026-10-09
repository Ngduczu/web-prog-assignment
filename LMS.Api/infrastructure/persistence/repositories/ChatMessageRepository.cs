using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.repositories;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence.Repositories;

public sealed class ChatMessageRepository : IChatMessageRepository
{
    private readonly AppDbContext _dbContext;
    public ChatMessageRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<ChatMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.ChatMessages.Include(message => message.Attachments).SingleOrDefaultAsync(message => message.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ChatMessage>> ListByRoomAsync(Guid roomId, int skip, int take, CancellationToken cancellationToken) =>
        await _dbContext.ChatMessages
            .AsNoTracking()
            .Include(message => message.Attachments)
            .Where(message => message.ChatRoomId == roomId && message.DeletedAt == null)
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, ChatRoomLatestMessage>> ListLatestByRoomAsync(
        IReadOnlyCollection<Guid> roomIds,
        CancellationToken cancellationToken)
    {
        if (roomIds.Count == 0) return new Dictionary<Guid, ChatRoomLatestMessage>();

        var latestMessages = await _dbContext.ChatMessages
            .AsNoTracking()
            .Where(message => roomIds.Contains(message.ChatRoomId) && message.DeletedAt == null)
            .GroupBy(message => message.ChatRoomId)
            .Select(group => group
                .OrderByDescending(message => message.CreatedAt)
                .ThenByDescending(message => message.Id)
                .Select(message => new ChatRoomLatestMessage(
                    message.ChatRoomId,
                    message.Id,
                    message.SenderId,
                    message.Content,
                    message.CreatedAt,
                    message.Attachments.Any()))
                .First())
            .ToListAsync(cancellationToken);

        return latestMessages.ToDictionary(message => message.ChatRoomId);
    }

    public async Task<IReadOnlySet<Guid>> ListUnreadRoomIdsAsync(
        Guid userId,
        IReadOnlyCollection<Guid> roomIds,
        CancellationToken cancellationToken)
    {
        if (roomIds.Count == 0) return new HashSet<Guid>();

        var latestIncomingMessages = await _dbContext.ChatMessages
            .AsNoTracking()
            .Where(message =>
                roomIds.Contains(message.ChatRoomId) &&
                message.SenderId != userId &&
                message.DeletedAt == null)
            .GroupBy(message => message.ChatRoomId)
            .Select(group => new { RoomId = group.Key, CreatedAt = group.Max(message => message.CreatedAt) })
            .ToListAsync(cancellationToken);

        var readReceipts = await (
                from participant in _dbContext.ChatParticipants.AsNoTracking()
                join message in _dbContext.ChatMessages.AsNoTracking()
                    on participant.LastReadMessageId equals (Guid?)message.Id
                where participant.UserId == userId && roomIds.Contains(participant.ChatRoomId)
                select new { participant.ChatRoomId, message.CreatedAt })
            .ToDictionaryAsync(item => item.ChatRoomId, item => item.CreatedAt, cancellationToken);

        return latestIncomingMessages
            .Where(item => !readReceipts.TryGetValue(item.RoomId, out var readAt) || item.CreatedAt > readAt)
            .Select(item => item.RoomId)
            .ToHashSet();
    }

    public void Add(ChatMessage message) => _dbContext.ChatMessages.Add(message);
}

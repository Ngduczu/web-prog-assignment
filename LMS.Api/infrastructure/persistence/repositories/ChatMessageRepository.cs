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

    public void Add(ChatMessage message) => _dbContext.ChatMessages.Add(message);
}

using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.Enums.Chats;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.Enums.Users;
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

    public async Task<IReadOnlyList<ChatRoom>> ListForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var role = await _dbContext.Users
            .Where(user => user.Id == userId)
            .Select(user => (Role?)user.Role)
            .SingleOrDefaultAsync(cancellationToken);

        Guid[] accessibleCourseIds = role switch
        {
            Role.Admin => await _dbContext.Courses
                .Select(course => course.Id)
                .ToArrayAsync(cancellationToken),
            Role.Teacher => await _dbContext.Courses
                .Where(course => course.TeacherId == userId)
                .Select(course => course.Id)
                .ToArrayAsync(cancellationToken),
            Role.Student => await _dbContext.Enrollments
                .Where(enrollment =>
                    enrollment.StudentId == userId &&
                    enrollment.Status == EnrollmentStatus.Accepted)
                .Select(enrollment => enrollment.CourseId)
                .ToArrayAsync(cancellationToken),
            _ => []
        };

        return await _dbContext.ChatRooms
            .Include(room => room.Participants)
            .Where(room =>
                room.Participants.Any(participant => participant.UserId == userId) ||
                room.Type == ChatRoomType.Course &&
                room.CourseId.HasValue &&
                accessibleCourseIds.Contains(room.CourseId.Value))
            .OrderByDescending(room => room.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ChatRoom?> EnsureCourseParticipantAsync(
        Guid roomId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await _dbContext.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO chat_participants (id, chat_room_id, user_id, joined_at, last_read_message_id)
            SELECT {{Guid.NewGuid()}}, room.id, {{userId}}, {{DateTime.UtcNow}}, NULL
            FROM chat_rooms AS room
            WHERE room.id = {{roomId}}
              AND room.type = 'Course'
              AND room.archived_at IS NULL
            ON CONFLICT (chat_room_id, user_id) DO NOTHING
            """, cancellationToken);

        // The room and its participant collection may already be tracked from
        // the access check. Reload them so callers receive the inserted member.
        _dbContext.ChangeTracker.Clear();
        return await GetByIdAsync(roomId, cancellationToken);
    }

    public void Add(ChatRoom room) => _dbContext.ChatRooms.Add(room);
}

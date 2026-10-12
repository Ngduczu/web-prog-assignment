using LMS.Api.domain.Enums.Chats;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Courses;

namespace LMS.Api.domain.Entities.Chats;

public class ChatRoom
{
    private readonly List<ChatParticipant> _participants = new();

    public Guid Id { get; private set; }
    public ChatRoomType Type { get; private set; }
    public Guid? CourseId { get; private set; }
    public string? DirectMessageKey { get; private set; }
    public Guid CreatedById { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ArchivedAt { get; private set; }
    public bool IsArchived => ArchivedAt.HasValue;
    public IReadOnlyCollection<ChatParticipant> Participants => _participants;

    private ChatRoom() { }

    public static ChatRoom CreateForCourse(Guid courseId, Guid createdById)
    {
        return new ChatRoom
        {
            Id = Guid.NewGuid(),
            Type = ChatRoomType.Course,
            CourseId = courseId,
            CreatedById = createdById,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static ChatRoom CreateDirectMessage(Guid firstUserId, Guid secondUserId)
    {
        var chatRoom = new ChatRoom
        {
            Id = Guid.NewGuid(),
            Type = ChatRoomType.DirectMessage,
            DirectMessageKey = BuildDirectMessageKey(firstUserId, secondUserId),
            CreatedById = firstUserId,
            CreatedAt = DateTime.UtcNow
        };

        chatRoom._participants.Add(new ChatParticipant(chatRoom.Id, firstUserId));
        chatRoom._participants.Add(new ChatParticipant(chatRoom.Id, secondUserId));

        return chatRoom;
    }

    public static string BuildDirectMessageKey(Guid firstUserId, Guid secondUserId)
    {
        if (firstUserId == secondUserId)
        {
            throw new ArgumentException("A direct chat room requires two different users.");
        }

        var first = firstUserId.CompareTo(secondUserId) < 0 ? firstUserId : secondUserId;
        var second = first == firstUserId ? secondUserId : firstUserId;

        return $"{first:N}:{second:N}";
    }

    public ChatParticipant AddCourseParticipant(Guid userId)
    {
        if (IsArchived)
        {
            throw new InvalidOperationException("Participants cannot be added to an archived chat room.");
        }

        if (Type != ChatRoomType.Course)
        {
            throw new InvalidOperationException("Participants can only be added explicitly to course chat rooms.");
        }

        var existingParticipant = _participants.SingleOrDefault(participant => participant.UserId == userId);
        if (existingParticipant is not null)
        {
            return existingParticipant;
        }

        var participant = new ChatParticipant(Id, userId);
        _participants.Add(participant);
        return participant;
    }

    public void ClearDirectConversation(Guid userId)
    {
        if (Type != ChatRoomType.DirectMessage)
        {
            throw new InvalidOperationException("Only direct conversations can be cleared.");
        }

        var participant = _participants.SingleOrDefault(item => item.UserId == userId)
            ?? throw new InvalidOperationException("Only a participant can clear this conversation.");
        participant.ClearConversation();
    }

    public bool RevealDirectConversation(Guid userId)
    {
        if (Type != ChatRoomType.DirectMessage)
        {
            return false;
        }

        var participant = _participants.SingleOrDefault(item => item.UserId == userId)
            ?? throw new InvalidOperationException("Only a participant can reveal this conversation.");
        if (participant.ConversationHiddenAt is null)
        {
            return false;
        }

        participant.RevealConversation();
        return true;
    }

    public void RevealDirectConversationForAll()
    {
        if (Type != ChatRoomType.DirectMessage)
        {
            return;
        }

        foreach (var participant in _participants)
        {
            participant.RevealConversation();
        }
    }

    public void Archive()
    {
        if (IsArchived)
        {
            return;
        }

        ArchivedAt = DateTime.UtcNow;
    }

    public void Restore()
    {
        ArchivedAt = null;
    }

    public bool SynchronizeWithCourse(Course course)
    {
        if (Type != ChatRoomType.Course || CourseId != course.Id)
        {
            throw new InvalidOperationException("Only the room's course can control its lifecycle.");
        }

        var shouldBeArchived = course.IsDeleted || course.Status == CourseStatus.Closed;
        if (shouldBeArchived == IsArchived)
        {
            return false;
        }

        if (shouldBeArchived)
        {
            Archive();
        }
        else
        {
            Restore();
        }

        return true;
    }
}

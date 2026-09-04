using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.Enums.Chats;

namespace LMS.Domain.Tests.Chats;

public class ChatRoomTests
{
    [Fact]
    public void DirectMessageKeyIsIndependentOfUserOrder()
    {
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();

        var firstKey = ChatRoom.BuildDirectMessageKey(firstUserId, secondUserId);
        var secondKey = ChatRoom.BuildDirectMessageKey(secondUserId, firstUserId);

        Assert.Equal(firstKey, secondKey);
    }

    [Fact]
    public void DirectMessageRoomContainsExactlyTwoParticipants()
    {
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();

        var chatRoom = ChatRoom.CreateDirectMessage(firstUserId, secondUserId);

        Assert.Equal(ChatRoomType.DirectMessage, chatRoom.Type);
        Assert.Equal(2, chatRoom.Participants.Count);
        Assert.Contains(chatRoom.Participants, participant => participant.UserId == firstUserId);
        Assert.Contains(chatRoom.Participants, participant => participant.UserId == secondUserId);
    }

    [Fact]
    public void DirectMessageRoomRejectsSameUser()
    {
        var userId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => ChatRoom.CreateDirectMessage(userId, userId));
    }

    [Fact]
    public void CourseParticipantIsAddedIdempotently()
    {
        var chatRoom = ChatRoom.CreateForCourse(Guid.NewGuid(), Guid.NewGuid());
        var userId = Guid.NewGuid();

        var firstParticipant = chatRoom.AddCourseParticipant(userId);
        var secondParticipant = chatRoom.AddCourseParticipant(userId);

        Assert.Same(firstParticipant, secondParticipant);
        Assert.Single(chatRoom.Participants);
    }
}

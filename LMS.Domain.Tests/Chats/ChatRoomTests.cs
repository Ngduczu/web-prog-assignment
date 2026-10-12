using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Chats;
using LMS.Api.domain.Enums.Courses;

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
    public void DirectConversationCanBeClearedAndRevealedForOneParticipant()
    {
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var chatRoom = ChatRoom.CreateDirectMessage(firstUserId, secondUserId);

        chatRoom.ClearDirectConversation(firstUserId);

        var firstParticipant = chatRoom.Participants.Single(participant => participant.UserId == firstUserId);
        var secondParticipant = chatRoom.Participants.Single(participant => participant.UserId == secondUserId);
        Assert.NotNull(firstParticipant.ConversationClearedAt);
        Assert.NotNull(firstParticipant.ConversationHiddenAt);
        Assert.Null(secondParticipant.ConversationClearedAt);
        Assert.Null(secondParticipant.ConversationHiddenAt);

        Assert.True(chatRoom.RevealDirectConversation(firstUserId));
        Assert.Null(firstParticipant.ConversationHiddenAt);
        Assert.NotNull(firstParticipant.ConversationClearedAt);
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

    [Fact]
    public void CourseRoomFollowsCourseStatusAndDeletionLifecycle()
    {
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var chatRoom = ChatRoom.CreateForCourse(course.Id, teacherId);

        Assert.False(chatRoom.SynchronizeWithCourse(course));

        course.SetStatus(CourseStatus.Closed);
        Assert.True(chatRoom.SynchronizeWithCourse(course));
        Assert.True(chatRoom.IsArchived);

        course.SetStatus(CourseStatus.Open);
        Assert.True(chatRoom.SynchronizeWithCourse(course));
        Assert.False(chatRoom.IsArchived);

        course.Delete(teacherId);
        Assert.True(chatRoom.SynchronizeWithCourse(course));
        Assert.True(chatRoom.IsArchived);

        course.Restore();
        Assert.True(chatRoom.SynchronizeWithCourse(course));
        Assert.False(chatRoom.IsArchived);
    }
}

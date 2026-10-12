using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.storage;
using LMS.Api.application.features.chats;
using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;
using Moq;

namespace LMS.Domain.Tests.Application;

public class ChatApplicationTests
{
    [Fact]
    public async Task ParticipantCanDeleteDirectConversationForThemselves()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var course = new Course("Course", null, otherUserId, 30, CourseStatus.Open, otherUserId);
        var room = ChatRoom.CreateDirectMessage(userId, otherUserId);
        var fixture = CreateFixture(userId, Role.Student, course, room);

        await fixture.Service.DeleteDirectConversationAsync(room.Id, CancellationToken.None);

        var participant = room.Participants.Single(item => item.UserId == userId);
        Assert.NotNull(participant.ConversationClearedAt);
        Assert.NotNull(participant.ConversationHiddenAt);
        Assert.Null(room.Participants.Single(item => item.UserId == otherUserId).ConversationHiddenAt);
        fixture.UnitOfWork.Verify(unitOfWork =>
            unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CourseConversationCannotBeDeleted()
    {
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var room = ChatRoom.CreateForCourse(course.Id, teacherId);
        room.AddCourseParticipant(teacherId);
        var fixture = CreateFixture(teacherId, Role.Teacher, course, room);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.DeleteDirectConversationAsync(room.Id, CancellationToken.None));
    }

    [Fact]
    public async Task AuthenticatedUserCanCreateShortLivedRealtimeToken()
    {
        var user = new User(
            "student@example.com",
            "Student",
            null,
            "hash",
            Role.Student,
            AccountStatus.Active);
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var room = ChatRoom.CreateForCourse(course.Id, teacherId);
        var fixture = CreateFixture(user.Id, Role.Student, course, room);
        fixture.Users.Setup(repository => repository.GetByIdAsync(
                user.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        var expiresAt = DateTime.UtcNow.AddMinutes(2);
        fixture.Tokens.Setup(provider => provider.CreateChatRealtimeToken(
                user.Id,
                user.Email,
                "Student",
                user.AuthVersion))
            .Returns(new AccessToken("chat-ticket", expiresAt));

        var result = await fixture.Service.CreateRealtimeTokenAsync(CancellationToken.None);

        Assert.Equal("chat-ticket", result.AccessToken);
        Assert.Equal(expiresAt, result.ExpiresAt);
    }

    [Fact]
    public async Task HistoricalCourseParticipantMustStillHaveCurrentCourseAccess()
    {
        var studentId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var room = ChatRoom.CreateForCourse(course.Id, teacherId);
        room.AddCourseParticipant(studentId);
        var enrollment = new Enrollment(studentId, course.Id, EnrollmentStatus.Waiting);
        var fixture = CreateFixture(studentId, Role.Student, course, room, enrollment);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            fixture.Service.EnsureRoomAccessAsync(room.Id, CancellationToken.None));

        fixture.Enrollments.Verify(repository => repository.GetByStudentAndCourseAsync(
            studentId,
            course.Id,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnrollmentFromAnotherCourseCannotAuthorizeRoomAccess()
    {
        var studentId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var allowedCourse = new Course("Allowed", null, teacherId, 30, CourseStatus.Open, teacherId);
        var targetCourse = new Course("Target", null, teacherId, 30, CourseStatus.Open, teacherId);
        var room = ChatRoom.CreateForCourse(targetCourse.Id, teacherId);
        room.AddCourseParticipant(studentId);
        var enrollment = new Enrollment(studentId, allowedCourse.Id);
        enrollment.Accept();
        var fixture = CreateFixture(studentId, Role.Student, targetCourse, room, enrollment);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            fixture.Service.EnsureRoomAccessAsync(room.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ExistingConnectionCannotSendAfterCoursePermissionIsRevoked()
    {
        var studentId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var room = ChatRoom.CreateForCourse(course.Id, teacherId);
        room.AddCourseParticipant(studentId);
        var downgradedEnrollment = new Enrollment(studentId, course.Id, EnrollmentStatus.Waiting);
        var fixture = CreateFixture(studentId, Role.Student, course, room, downgradedEnrollment);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            fixture.Service.SendMessageAsync(
                room.Id,
                new SendChatMessageRequest("should be rejected", null),
                CancellationToken.None));

        fixture.Messages.Verify(repository => repository.Add(It.IsAny<ChatMessage>()), Times.Never);
    }

    [Fact]
    public async Task RoomListOmitsCourseRoomWhenCurrentCourseAccessWasRevoked()
    {
        var studentId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var room = ChatRoom.CreateForCourse(course.Id, teacherId);
        room.AddCourseParticipant(studentId);
        var directRoom = ChatRoom.CreateDirectMessage(studentId, Guid.NewGuid());
        var fixture = CreateFixture(
            studentId,
            Role.Student,
            course,
            room,
            listedRooms: [room, directRoom]);

        var result = await fixture.Service.ListRoomsAsync(CancellationToken.None);

        var returnedRoom = Assert.Single(result);
        Assert.Equal(directRoom.Id, returnedRoom.Id);
    }

    [Fact]
    public async Task RoomListMarksRoomsThatContainUnreadMessages()
    {
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var room = ChatRoom.CreateForCourse(course.Id, teacherId);
        room.AddCourseParticipant(teacherId);
        var fixture = CreateFixture(teacherId, Role.Teacher, course, room);
        fixture.Messages.Setup(repository => repository.ListUnreadRoomIdsAsync(
                teacherId,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(room.Id)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { room.Id });

        var result = await fixture.Service.ListRoomsAsync(CancellationToken.None);

        Assert.True(Assert.Single(result).HasUnread);
    }

    [Fact]
    public async Task RoomListIncludesLatestMessageAndSortsByRecentActivity()
    {
        var userId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var quietRoom = ChatRoom.CreateDirectMessage(userId, Guid.NewGuid());
        var activeRoom = ChatRoom.CreateDirectMessage(userId, Guid.NewGuid());
        var fixture = CreateFixture(
            userId,
            Role.Teacher,
            course,
            quietRoom,
            listedRooms: [quietRoom, activeRoom]);
        var quietTime = DateTime.UtcNow.AddMinutes(-5);
        var activeTime = DateTime.UtcNow;
        fixture.Messages.Setup(repository => repository.ListLatestByRoomAsync(
                userId,
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ChatRoomLatestMessage>
            {
                [quietRoom.Id] = new(quietRoom.Id, Guid.NewGuid(), Guid.NewGuid(), "Older message", quietTime, false),
                [activeRoom.Id] = new(activeRoom.Id, Guid.NewGuid(), Guid.NewGuid(), "Latest message", activeTime, true)
            });

        var result = await fixture.Service.ListRoomsAsync(CancellationToken.None);

        Assert.Equal(activeRoom.Id, result[0].Id);
        Assert.Equal("Latest message", result[0].LatestMessage?.Content);
        Assert.True(result[0].LatestMessage?.HasAttachments);
        Assert.Equal(quietRoom.Id, result[1].Id);
    }

    [Fact]
    public async Task RoomListOmitsDeletedCourseRoomAndContinuesWithOtherRooms()
    {
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var room = ChatRoom.CreateForCourse(course.Id, teacherId);
        room.AddCourseParticipant(teacherId);
        var directRoom = ChatRoom.CreateDirectMessage(teacherId, Guid.NewGuid());
        course.Delete(teacherId);
        var fixture = CreateFixture(
            teacherId,
            Role.Teacher,
            course,
            room,
            listedRooms: [room, directRoom]);

        var result = await fixture.Service.ListRoomsAsync(CancellationToken.None);

        var returnedRoom = Assert.Single(result);
        Assert.Equal(directRoom.Id, returnedRoom.Id);
        Assert.True(room.IsArchived);
        fixture.UnitOfWork.Verify(unitOfWork =>
            unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RoomListOmitsRoomWhoseCourseNoLongerExistsAndContinuesWithOtherRooms()
    {
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var room = ChatRoom.CreateForCourse(course.Id, teacherId);
        room.AddCourseParticipant(teacherId);
        var directRoom = ChatRoom.CreateDirectMessage(teacherId, Guid.NewGuid());
        var fixture = CreateFixture(
            teacherId,
            Role.Teacher,
            course,
            room,
            listedRooms: [room, directRoom]);
        fixture.Courses.Setup(repository => repository.ListByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Course>());

        var result = await fixture.Service.ListRoomsAsync(CancellationToken.None);

        var returnedRoom = Assert.Single(result);
        Assert.Equal(directRoom.Id, returnedRoom.Id);
        Assert.False(room.IsArchived);
        fixture.UnitOfWork.Verify(unitOfWork =>
            unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RoomAccessArchivesClosedCourseAndRestoresReopenedCourse()
    {
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Closed, teacherId);
        var room = ChatRoom.CreateForCourse(course.Id, teacherId);
        room.AddCourseParticipant(teacherId);
        var fixture = CreateFixture(teacherId, Role.Teacher, course, room);

        await fixture.Service.EnsureRoomAccessAsync(room.Id, CancellationToken.None);
        Assert.True(room.IsArchived);

        course.SetStatus(CourseStatus.Open);
        await fixture.Service.EnsureRoomAccessAsync(room.Id, CancellationToken.None);
        Assert.False(room.IsArchived);

        fixture.UnitOfWork.Verify(unitOfWork =>
            unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ArchivedCourseRoomCanBeReadButCannotBeJoinedOrWritten()
    {
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Closed, teacherId);
        var room = ChatRoom.CreateForCourse(course.Id, teacherId);
        room.AddCourseParticipant(teacherId);
        var fixture = CreateFixture(teacherId, Role.Teacher, course, room);

        await fixture.Service.EnsureRoomAccessAsync(room.Id, CancellationToken.None);
        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.EnsureRoomJoinAccessAsync(room.Id, CancellationToken.None));
        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.SendMessageAsync(
                room.Id,
                new SendChatMessageRequest("should be rejected", null),
                CancellationToken.None));

        Assert.True(room.IsArchived);
        fixture.Messages.Verify(repository => repository.Add(It.IsAny<ChatMessage>()), Times.Never);
    }

    [Fact]
    public async Task DeletedCourseArchivesRoomBeforeAccessIsRejected()
    {
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var room = ChatRoom.CreateForCourse(course.Id, teacherId);
        room.AddCourseParticipant(teacherId);
        course.Delete(teacherId);
        var fixture = CreateFixture(teacherId, Role.Teacher, course, room);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.EnsureRoomAccessAsync(room.Id, CancellationToken.None));

        Assert.True(room.IsArchived);
        fixture.UnitOfWork.Verify(unitOfWork =>
            unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MissingCourseRejectsExplicitCourseRoomAccess()
    {
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var room = ChatRoom.CreateForCourse(course.Id, teacherId);
        room.AddCourseParticipant(teacherId);
        var fixture = CreateFixture(teacherId, Role.Teacher, course, room);
        fixture.Courses.Setup(repository => repository.GetByIdAsync(
                course.Id,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Course?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Service.EnsureRoomAccessAsync(room.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RoomListLoadsCoursesAndEnrollmentsInBatchedQueries()
    {
        var teacherId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var room = ChatRoom.CreateForCourse(course.Id, teacherId);
        room.AddCourseParticipant(studentId);
        var enrollment = new Enrollment(studentId, course.Id);
        enrollment.Accept();
        var directRoom = ChatRoom.CreateDirectMessage(studentId, teacherId);
        var fixture = CreateFixture(
            studentId,
            Role.Student,
            course,
            room,
            enrollment,
            listedRooms: [room, directRoom]);

        var result = await fixture.Service.ListRoomsAsync(CancellationToken.None);

        Assert.Equal(2, result.Count);
        fixture.Courses.Verify(repository => repository.ListByIdsAsync(
            It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Enrollments.Verify(repository => repository.ListByStudentAsync(
            studentId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RoomListOmitsCourseRoomForTeacherWhoDoesNotOwnCourse()
    {
        var ownerId = Guid.NewGuid();
        var otherTeacherId = Guid.NewGuid();
        var course = new Course("Course", null, ownerId, 30, CourseStatus.Open, ownerId);
        var room = ChatRoom.CreateForCourse(course.Id, ownerId);
        room.AddCourseParticipant(otherTeacherId);
        var fixture = CreateFixture(otherTeacherId, Role.Teacher, course, room);

        var result = await fixture.Service.ListRoomsAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task AuthorizedParticipantCanDownloadMessageAttachment()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var course = new Course("Course", null, otherUserId, 30, CourseStatus.Open, otherUserId);
        var room = ChatRoom.CreateDirectMessage(userId, otherUserId);
        var message = ChatMessage.CreateWithAttachment(
            room.Id,
            otherUserId,
            null,
            "notes.pdf",
            "chat/notes.pdf",
            "application/pdf",
            3);
        var attachment = Assert.Single(message.Attachments);
        var fixture = CreateFixture(userId, Role.Student, course, room);
        fixture.Messages.Setup(repository => repository.GetByIdAsync(message.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);
        fixture.FileStorage.Setup(storage => storage.OpenReadAsync(attachment.ObjectKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream([1, 2, 3]));

        var result = await fixture.Service.GetAttachmentContentAsync(
            message.Id,
            attachment.Id,
            CancellationToken.None);

        Assert.Equal("notes.pdf", result.FileName);
        Assert.Equal("application/pdf", result.ContentType);
        fixture.FileStorage.Verify(storage => storage.OpenReadAsync(
            attachment.ObjectKey,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MessageListOmitsDeletedMessagesAndTheirAttachments()
    {
        var userId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var room = ChatRoom.CreateDirectMessage(userId, teacherId);
        var visible = ChatMessage.CreateText(room.Id, userId, "visible");
        var deleted = ChatMessage.CreateWithAttachment(
            room.Id,
            userId,
            "deleted",
            "secret.pdf",
            "chat/secret.pdf",
            "application/pdf",
            100);
        deleted.Delete();
        var fixture = CreateFixture(userId, Role.Student, course, room);
        fixture.Messages.Setup(repository => repository.ListByRoomAsync(
                room.Id,
                0,
                20,
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([deleted, visible]);

        var result = await fixture.Service.ListMessagesAsync(
            room.Id,
            new ChatPageQuery(),
            CancellationToken.None);

        var returnedMessage = Assert.Single(result);
        Assert.Equal(visible.Id, returnedMessage.Id);
        Assert.Empty(returnedMessage.Attachments);
    }

    [Fact]
    public async Task CourseMessageListIncludesSenderIdentity()
    {
        var studentId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var room = ChatRoom.CreateForCourse(course.Id, teacherId);
        room.AddCourseParticipant(studentId);
        var enrollment = new Enrollment(studentId, course.Id);
        enrollment.Accept();
        var teacher = new User(
            "teacher@example.com",
            "Course teacher",
            null,
            "hash",
            Role.Teacher,
            AccountStatus.Active);
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(teacher, teacherId);
        var message = ChatMessage.CreateText(room.Id, teacherId, "Welcome");
        var fixture = CreateFixture(studentId, Role.Student, course, room, enrollment);
        fixture.Messages.Setup(repository => repository.ListByRoomAsync(
                room.Id,
                0,
                20,
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);
        fixture.Users.Setup(repository => repository.ListByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(teacherId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([teacher]);

        var result = await fixture.Service.ListMessagesAsync(
            room.Id,
            new ChatPageQuery(),
            CancellationToken.None);

        var returnedMessage = Assert.Single(result);
        Assert.Equal("Course teacher", returnedMessage.SenderFullName);
        Assert.Equal("Teacher", returnedMessage.SenderRole);
    }

    [Fact]
    public async Task AttachmentCleanupFailuresPreserveOriginalMessageSaveFailure()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var course = new Course("Course", null, otherUserId, 30, CourseStatus.Open, otherUserId);
        var room = ChatRoom.CreateDirectMessage(userId, otherUserId);
        var fixture = CreateFixture(userId, Role.Student, course, room);
        var persistenceFailure = new InvalidOperationException("Message database failure");
        fixture.UnitOfWork
            .Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(persistenceFailure);
        fixture.FileStorage
            .Setup(storage => storage.UploadAsync(
                It.IsAny<FileUpload>(),
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredFile("chat.pdf", "chat/object.pdf", "application/pdf", 3));
        fixture.FileCleanup
            .Setup(queue => queue.EnqueueImmediatelyAsync(
                "chat/object.pdf",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cleanup queue failure"));
        fixture.FileStorage
            .Setup(storage => storage.DeleteIfExistsAsync(
                "chat/object.pdf",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage cleanup failure"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.SendMessageWithAttachmentAsync(
                room.Id,
                null,
                null,
                "chat.pdf",
                "application/pdf",
                3,
                new MemoryStream([1, 2, 3]),
                CancellationToken.None));

        Assert.Same(persistenceFailure, exception);
        fixture.FileCleanup.Verify(queue => queue.EnqueueImmediatelyAsync(
            "chat/object.pdf",
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.FileStorage.Verify(storage => storage.DeleteIfExistsAsync(
            "chat/object.pdf",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadReceiptNeverMovesBackward()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var course = new Course("Course", null, otherUserId, 30, CourseStatus.Open, otherUserId);
        var room = ChatRoom.CreateDirectMessage(userId, otherUserId);
        var older = ChatMessage.CreateText(room.Id, otherUserId, "older");
        var newer = ChatMessage.CreateText(room.Id, otherUserId, "newer");
        SetCreatedAt(older, new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        SetCreatedAt(newer, new DateTime(2030, 1, 1, 0, 1, 0, DateTimeKind.Utc));
        var fixture = CreateFixture(userId, Role.Student, course, room);
        fixture.Messages.Setup(repository => repository.GetByIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => id == older.Id ? older : newer);

        await fixture.Service.MarkAsReadAsync(room.Id, newer.Id, CancellationToken.None);
        await fixture.Service.MarkAsReadAsync(room.Id, older.Id, CancellationToken.None);

        Assert.Equal(
            newer.Id,
            room.Participants.Single(participant => participant.UserId == userId).LastReadMessageId);
        fixture.UnitOfWork.Verify(unitOfWork =>
            unitOfWork.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletedMessageCannotBecomeReadReceiptTarget()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var course = new Course("Course", null, otherUserId, 30, CourseStatus.Open, otherUserId);
        var room = ChatRoom.CreateDirectMessage(userId, otherUserId);
        var deleted = ChatMessage.CreateText(room.Id, otherUserId, "deleted");
        deleted.Delete();
        var fixture = CreateFixture(userId, Role.Student, course, room);
        fixture.Messages.Setup(repository => repository.GetByIdAsync(
                deleted.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(deleted);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.MarkAsReadAsync(room.Id, deleted.Id, CancellationToken.None));

        Assert.Null(room.Participants.Single(participant => participant.UserId == userId).LastReadMessageId);
    }

    [Fact]
    public async Task GetOrCreateCourseRoomAddsBothCreatorAndTeacher()
    {
        var studentId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var enrollment = new Enrollment(studentId, course.Id);
        enrollment.Accept();
        var existingRoom = ChatRoom.CreateForCourse(course.Id, teacherId);
        var fixture = CreateFixture(studentId, Role.Student, course, existingRoom, enrollment);
        fixture.Rooms.Setup(r => r.GetByCourseIdAsync(course.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoom?)null);

        var result = await fixture.Service.GetOrCreateCourseRoomAsync(course.Id, CancellationToken.None);

        Assert.Equal(course.Id, result.CourseId);
        Assert.Contains(studentId, result.ParticipantIds);
        Assert.Contains(teacherId, result.ParticipantIds);
        fixture.Rooms.Verify(r => r.Add(It.Is<ChatRoom>(room =>
            room.Participants.Any(p => p.UserId == studentId) &&
            room.Participants.Any(p => p.UserId == teacherId))), Times.Once);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CourseParticipantIsAutomaticallyAddedWhenAccessingCourseRoom()
    {
        var creatorId = Guid.NewGuid();
        var otherStudentId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var course = new Course("Course", null, teacherId, 30, CourseStatus.Open, teacherId);
        var room = ChatRoom.CreateForCourse(course.Id, creatorId);
        room.AddCourseParticipant(creatorId);

        var enrollment = new Enrollment(otherStudentId, course.Id);
        enrollment.Accept();
        var fixture = CreateFixture(otherStudentId, Role.Student, course, room, enrollment);

        Assert.DoesNotContain(room.Participants, p => p.UserId == otherStudentId);

        var joined = await fixture.Service.JoinRoomAsync(room.Id, CancellationToken.None);

        Assert.Contains(room.Participants, p => p.UserId == otherStudentId);
        Assert.Contains(otherStudentId, joined.ParticipantIds);
        fixture.Rooms.Verify(repository => repository.EnsureCourseParticipantAsync(
            room.Id,
            otherStudentId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static void SetCreatedAt(ChatMessage message, DateTime createdAt) =>
        typeof(ChatMessage).GetProperty(nameof(ChatMessage.CreatedAt))!.SetValue(message, createdAt);

    private static Fixture CreateFixture(
        Guid userId,
        Role role,
        Course course,
        ChatRoom room,
        Enrollment? enrollment = null,
        IReadOnlyList<ChatRoom>? listedRooms = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        currentUser.SetupGet(user => user.UserId).Returns(userId);
        currentUser.SetupGet(user => user.Role).Returns(role);

        var rooms = new Mock<IChatRoomRepository>();
        rooms.Setup(repository => repository.GetByIdAsync(room.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(room);
        rooms.Setup(repository => repository.ListForUserAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(listedRooms ?? [room]);
        rooms.Setup(repository => repository.EnsureCourseParticipantAsync(
                room.Id,
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, Guid, CancellationToken>((_, participantId, _) =>
            {
                room.AddCourseParticipant(participantId);
                return Task.FromResult<ChatRoom?>(room);
            });

        var courses = new Mock<ICourseRepository>();
        courses.Setup(repository => repository.GetByIdAsync(course.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(course);
        courses.Setup(repository => repository.ListByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([course]);

        var enrollments = new Mock<IEnrollmentRepository>();
        enrollments.Setup(repository => repository.GetByStudentAndCourseAsync(
                userId,
                course.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(enrollment);
        enrollments.Setup(repository => repository.ListByStudentAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(enrollment is null ? [] : [enrollment]);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var messages = new Mock<IChatMessageRepository>();
        messages.Setup(repository => repository.ListUnreadRoomIdsAsync(
                userId,
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());
        messages.Setup(repository => repository.ListLatestByRoomAsync(
                userId,
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ChatRoomLatestMessage>());

        var fileStorage = new Mock<IFileStorage>();
        var fileCleanup = new Mock<IFileCleanupQueue>();
        var tokens = new Mock<ITokenProvider>();
        var users = new Mock<IUserRepository>();
        users.Setup(repository => repository.ListByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new ChatService(
            currentUser.Object,
            rooms.Object,
            messages.Object,
            courses.Object,
            enrollments.Object,
            users.Object,
            unitOfWork.Object,
            fileStorage.Object,
            tokens.Object,
            fileCleanup.Object,
            transactionManager: null);

        return new Fixture(service, rooms, courses, enrollments, users, messages, unitOfWork, fileStorage, fileCleanup, tokens);
    }

    private sealed record Fixture(
        ChatService Service,
        Mock<IChatRoomRepository> Rooms,
        Mock<ICourseRepository> Courses,
        Mock<IEnrollmentRepository> Enrollments,
        Mock<IUserRepository> Users,
        Mock<IChatMessageRepository> Messages,
        Mock<IUnitOfWork> UnitOfWork,
        Mock<IFileStorage> FileStorage,
        Mock<IFileCleanupQueue> FileCleanup,
        Mock<ITokenProvider> Tokens);
}

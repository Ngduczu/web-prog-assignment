using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.storage;
using LMS.Api.application.features.chats;
using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;
using Moq;

namespace LMS.Domain.Tests.Application;

public class ChatApplicationTests
{
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

        var service = new ChatService(
            currentUser.Object,
            rooms.Object,
            messages.Object,
            courses.Object,
            enrollments.Object,
            Mock.Of<IUserRepository>(),
            unitOfWork.Object,
            Mock.Of<IFileStorage>());

        return new Fixture(service, courses, enrollments, messages, unitOfWork);
    }

    private sealed record Fixture(
        ChatService Service,
        Mock<ICourseRepository> Courses,
        Mock<IEnrollmentRepository> Enrollments,
        Mock<IChatMessageRepository> Messages,
        Mock<IUnitOfWork> UnitOfWork);
}

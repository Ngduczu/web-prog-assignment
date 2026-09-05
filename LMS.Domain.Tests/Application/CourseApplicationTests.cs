using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.features.courses;
using LMS.Api.application.features.enrollments;
using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Domain.Tests.Application;

public class CourseApplicationTests
{
    [Fact]
    public async Task TeacherCanCreateCourseAssignedToSelf()
    {
        var teacher = CreateUser(Role.Teacher);
        var courses = new FakeCourseRepository();
        var unitOfWork = new FakeUnitOfWork();
        var service = CreateCourseService(teacher, courses, unitOfWork);

        var result = await service.CreateAsync(new CreateCourseRequest(
            "  Domain Driven Design  ",
            "  Course description  ",
            teacher.Id,
            30));

        Assert.Equal("Domain Driven Design", result.Name);
        Assert.Equal("Course description", result.Description);
        Assert.Equal(teacher.Id, result.TeacherId);
        Assert.Single(courses.Items);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task TeacherCannotCreateCourseForAnotherTeacher()
    {
        var teacher = CreateUser(Role.Teacher);
        var otherTeacher = CreateUser(Role.Teacher);
        var users = new FakeUserRepository(teacher, otherTeacher);
        var service = new CourseService(
            new FakeCourseRepository(),
            users,
            new FakeEnrollmentRepository(),
            new FakeUnitOfWork(),
            new FakeCurrentUser(teacher),
            new FakeChatRoomRepository());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CreateAsync(new CreateCourseRequest("Course", null, otherTeacher.Id, 20)));
    }

    [Fact]
    public async Task InactiveTeacherCannotBeAssignedToCourse()
    {
        var admin = CreateUser(Role.Admin);
        var inactiveTeacher = CreateUser(Role.Teacher, AccountStatus.Inactive);
        var users = new FakeUserRepository(admin, inactiveTeacher);
        var service = new CourseService(
            new FakeCourseRepository(),
            users,
            new FakeEnrollmentRepository(),
            new FakeUnitOfWork(),
            new FakeCurrentUser(admin),
            new FakeChatRoomRepository());

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.CreateAsync(new CreateCourseRequest("Course", null, inactiveTeacher.Id, 20)));
    }

    [Fact]
    public async Task StudentCanOnlyListOpenCourses()
    {
        var teacher = CreateUser(Role.Teacher);
        var student = CreateUser(Role.Student);
        var openCourse = CreateCourse(teacher);
        var closedCourse = CreateCourse(teacher);
        closedCourse.SetStatus(CourseStatus.Closed);
        var service = new CourseService(
            new FakeCourseRepository(openCourse, closedCourse),
            new FakeUserRepository(teacher),
            new FakeEnrollmentRepository(),
            new FakeUnitOfWork(),
            new FakeCurrentUser(student),
            new FakeChatRoomRepository());

        var result = await service.ListAsync(new CourseQuery(Status: CourseStatus.Closed));

        var course = Assert.Single(result.Items);
        Assert.Equal(openCourse.Id, course.Id);
        Assert.Equal(CourseStatus.Open, course.Status);
    }

    [Fact]
    public async Task StudentCannotFetchClosedCourseById()
    {
        var teacher = CreateUser(Role.Teacher);
        var student = CreateUser(Role.Student);
        var closedCourse = CreateCourse(teacher);
        closedCourse.SetStatus(CourseStatus.Closed);
        var service = new CourseService(
            new FakeCourseRepository(closedCourse),
            new FakeUserRepository(teacher),
            new FakeEnrollmentRepository(),
            new FakeUnitOfWork(),
            new FakeCurrentUser(student),
            new FakeChatRoomRepository());

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(closedCourse.Id));
    }

    [Fact]
    public async Task StudentCanViewClosedCourseWhenEnrollmentAccepted()
    {
        var teacher = CreateUser(Role.Teacher);
        var student = CreateUser(Role.Student);
        var closedCourse = CreateCourse(teacher);
        closedCourse.SetStatus(CourseStatus.Closed);
        var accepted = new Enrollment(student.Id, closedCourse.Id);
        accepted.Accept();
        var service = new CourseService(
            new FakeCourseRepository(closedCourse),
            new FakeUserRepository(teacher),
            new FakeEnrollmentRepository(accepted),
            new FakeUnitOfWork(),
            new FakeCurrentUser(student),
            new FakeChatRoomRepository());

        var result = await service.GetByIdAsync(closedCourse.Id);

        Assert.Equal(closedCourse.Id, result.Id);
    }

    [Fact]
    public async Task CourseChangesSynchronizeTheCourseRoomLifecycle()
    {
        var teacher = CreateUser(Role.Teacher);
        var course = CreateCourse(teacher);
        var room = ChatRoom.CreateForCourse(course.Id, teacher.Id);
        var rooms = new FakeChatRoomRepository(room);
        var service = new CourseService(
            new FakeCourseRepository(course),
            new FakeUserRepository(teacher),
            new FakeEnrollmentRepository(),
            new FakeUnitOfWork(),
            new FakeCurrentUser(teacher),
            rooms);

        await service.UpdateAsync(
            course.Id,
            new UpdateCourseRequest("Course", null, 30, CourseStatus.Closed));
        Assert.True(room.IsArchived);

        await service.UpdateAsync(
            course.Id,
            new UpdateCourseRequest("Course", null, 30, CourseStatus.Open));
        Assert.False(room.IsArchived);

        await service.DeleteAsync(course.Id);
        Assert.True(room.IsArchived);

        await service.RestoreAsync(course.Id);
        Assert.False(room.IsArchived);
    }

    [Fact]
    public async Task StudentCanRequestEnrollmentForOpenCourse()
    {
        var teacher = CreateUser(Role.Teacher);
        var student = CreateUser(Role.Student);
        var course = CreateCourse(teacher);
        var enrollments = new FakeEnrollmentRepository();
        var unitOfWork = new FakeUnitOfWork();
        var service = new EnrollmentService(
            enrollments,
            new FakeCourseRepository(course),
            unitOfWork,
            new FakeCurrentUser(student));

        var result = await service.RequestAsync(course.Id);

        Assert.Equal(EnrollmentStatus.Waiting, result.Status);
        Assert.Equal(student.Id, result.StudentId);
        Assert.Single(enrollments.Items);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task DuplicateEnrollmentRequestIsRejected()
    {
        var teacher = CreateUser(Role.Teacher);
        var student = CreateUser(Role.Student);
        var course = CreateCourse(teacher);
        var existing = new Enrollment(student.Id, course.Id);
        var service = new EnrollmentService(
            new FakeEnrollmentRepository(existing),
            new FakeCourseRepository(course),
            new FakeUnitOfWork(),
            new FakeCurrentUser(student));

        await Assert.ThrowsAsync<ConflictException>(() => service.RequestAsync(course.Id));
    }

    [Fact]
    public async Task CourseCapacityIsCheckedBeforeAcceptingEnrollment()
    {
        var teacher = CreateUser(Role.Teacher);
        var firstStudent = CreateUser(Role.Student);
        var secondStudent = CreateUser(Role.Student);
        var course = CreateCourse(teacher, maxStudents: 1);
        var accepted = new Enrollment(firstStudent.Id, course.Id);
        accepted.Accept();
        var waiting = new Enrollment(secondStudent.Id, course.Id);
        var service = new EnrollmentService(
            new FakeEnrollmentRepository(accepted, waiting),
            new FakeCourseRepository(course),
            new FakeUnitOfWork(),
            new FakeCurrentUser(teacher));

        await Assert.ThrowsAsync<ConflictException>(() => service.AcceptAsync(waiting.Id));
        Assert.Equal(EnrollmentStatus.Waiting, waiting.Status);
    }

    [Fact]
    public async Task AcceptEnrollmentRunsInsideSerializableTransaction()
    {
        var teacher = CreateUser(Role.Teacher);
        var student = CreateUser(Role.Student);
        var course = CreateCourse(teacher);
        var enrollment = new Enrollment(student.Id, course.Id);
        var transactionManager = new FakeTransactionManager();
        var service = new EnrollmentService(
            new FakeEnrollmentRepository(enrollment),
            new FakeCourseRepository(course),
            new FakeUnitOfWork(),
            new FakeCurrentUser(teacher),
            transactionManager);

        var result = await service.AcceptAsync(enrollment.Id);

        Assert.Equal(EnrollmentStatus.Accepted, result.Status);
        Assert.Equal(1, transactionManager.SerializableExecutionCount);
    }

    [Fact]
    public async Task StudentCannotCancelAnotherStudentsEnrollment()
    {
        var teacher = CreateUser(Role.Teacher);
        var student = CreateUser(Role.Student);
        var otherStudent = CreateUser(Role.Student);
        var course = CreateCourse(teacher);
        var enrollment = new Enrollment(otherStudent.Id, course.Id);
        var service = new EnrollmentService(
            new FakeEnrollmentRepository(enrollment),
            new FakeCourseRepository(course),
            new FakeUnitOfWork(),
            new FakeCurrentUser(student));

        await Assert.ThrowsAsync<ForbiddenException>(() => service.CancelAsync(enrollment.Id));
    }

    private static CourseService CreateCourseService(
        User actor,
        FakeCourseRepository courses,
        FakeUnitOfWork unitOfWork) =>
        new(
            courses,
            new FakeUserRepository(actor),
            new FakeEnrollmentRepository(),
            unitOfWork,
            new FakeCurrentUser(actor),
            new FakeChatRoomRepository());

    private static User CreateUser(
        Role role,
        AccountStatus status = AccountStatus.Active) =>
        new(
            $"{Guid.NewGuid():N}@example.com",
            "Test User",
            null,
            null,
            role,
            status);

    private static Course CreateCourse(User teacher, int maxStudents = 30) =>
        new("Course", null, teacher.Id, maxStudents, CourseStatus.Open, teacher.Id);

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(User user)
        {
            UserId = user.Id;
            Role = user.Role;
        }

        public bool IsAuthenticated => true;
        public Guid? UserId { get; }
        public Role? Role { get; }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeTransactionManager : ITransactionManager
    {
        public int SerializableExecutionCount { get; private set; }

        public Task ExecuteAsync(
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken = default) =>
            action(cancellationToken);

        public Task ExecuteSerializableAsync(
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken = default)
        {
            SerializableExecutionCount++;
            return action(cancellationToken);
        }
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly IReadOnlyDictionary<Guid, User> _users;

        public FakeUserRepository(params User[] users)
        {
            _users = users.ToDictionary(user => user.Id);
        }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_users.GetValueOrDefault(id));

        public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(_users.Values.SingleOrDefault(user => user.Email == normalizedEmail));

        public Task<User?> GetByExternalLoginAsync(ExternalProvider provider, string externalUserId, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(null);

        public Task<ExternalUser?> GetExternalLoginAsync(Guid userId, ExternalProvider provider, CancellationToken cancellationToken) =>
            Task.FromResult<ExternalUser?>(null);

        public void Add(User user)
        {
            throw new NotSupportedException();
        }

        public void AddExternalUser(ExternalUser externalUser) => throw new NotSupportedException();
    }

    private sealed class FakeCourseRepository : ICourseRepository
    {
        public FakeCourseRepository(params Course[] courses)
        {
            Items.AddRange(courses);
        }

        public List<Course> Items { get; } = [];

        public Task<Course?> GetByIdAsync(
            Guid id,
            bool includeDeleted,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(course =>
                course.Id == id && (includeDeleted || !course.IsDeleted)));

        public Task<IReadOnlyList<Course>> ListAsync(
            string? search,
            CourseStatus? status,
            Guid? teacherId,
            bool includeDeleted,
            int skip,
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Course>>(
                Filter(search, status, teacherId, includeDeleted).Skip(skip).Take(take).ToArray());

        public Task<int> CountAsync(
            string? search,
            CourseStatus? status,
            Guid? teacherId,
            bool includeDeleted,
            CancellationToken cancellationToken) =>
            Task.FromResult(Filter(search, status, teacherId, includeDeleted).Count());

        public void Add(Course course)
        {
            Items.Add(course);
        }

        private IEnumerable<Course> Filter(
            string? search,
            CourseStatus? status,
            Guid? teacherId,
            bool includeDeleted) =>
            Items.Where(course =>
                (includeDeleted || !course.IsDeleted) &&
                (search is null || course.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
                (status is null || course.Status == status) &&
                (teacherId is null || course.TeacherId == teacherId));
    }

    private sealed class FakeEnrollmentRepository : IEnrollmentRepository
    {
        public FakeEnrollmentRepository(params Enrollment[] enrollments)
        {
            Items.AddRange(enrollments);
        }

        public List<Enrollment> Items { get; } = [];

        public Task<Enrollment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(enrollment => enrollment.Id == id));

        public Task<Enrollment?> GetByStudentAndCourseAsync(
            Guid studentId,
            Guid courseId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(enrollment =>
                enrollment.StudentId == studentId && enrollment.CourseId == courseId));

        public Task<IReadOnlyList<Enrollment>> ListByCourseAsync(
            Guid courseId,
            int skip,
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Enrollment>>(
                Items.Where(enrollment => enrollment.CourseId == courseId).Skip(skip).Take(take).ToArray());

        public Task<int> CountByCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Count(enrollment => enrollment.CourseId == courseId));

        public Task<int> CountAcceptedByCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Count(enrollment =>
                enrollment.CourseId == courseId && enrollment.Status == EnrollmentStatus.Accepted));

        public void Add(Enrollment enrollment)
        {
            Items.Add(enrollment);
        }
    }

    private sealed class FakeChatRoomRepository : IChatRoomRepository
    {
        private readonly List<ChatRoom> _rooms = [];

        public FakeChatRoomRepository(params ChatRoom[] rooms)
        {
            _rooms.AddRange(rooms);
        }

        public Task<ChatRoom?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_rooms.SingleOrDefault(room => room.Id == id));

        public Task<ChatRoom?> GetByCourseIdAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(_rooms.SingleOrDefault(room => room.CourseId == courseId));

        public Task<ChatRoom?> GetByDirectMessageKeyAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(_rooms.SingleOrDefault(room => room.DirectMessageKey == key));

        public Task<IReadOnlyList<ChatRoom>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChatRoom>>(
                _rooms.Where(room => room.Participants.Any(participant => participant.UserId == userId)).ToArray());

        public void Add(ChatRoom room) => _rooms.Add(room);
    }
}

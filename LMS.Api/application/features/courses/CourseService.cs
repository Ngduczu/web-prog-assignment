using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.common;
using LMS.Api.domain.Entities.Chats;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Api.application.features.courses;

public sealed class CourseService : ICourseService
{
    private const int MaximumPageSize = 100;
    private readonly ICourseRepository _courses;
    private readonly IUserRepository _users;
    private readonly IEnrollmentRepository _enrollments;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IChatRoomRepository _chatRooms;
    private readonly ITransactionManager? _transactionManager;

    public CourseService(
        ICourseRepository courses,
        IUserRepository users,
        IEnrollmentRepository enrollments,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IChatRoomRepository chatRooms,
        ITransactionManager? transactionManager = null)
    {
        _courses = courses;
        _users = users;
        _enrollments = enrollments;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _chatRooms = chatRooms;
        _transactionManager = transactionManager;
    }

    public async Task<CourseDto> CreateAsync(
        CreateCourseRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateCourse(request.Name, request.Description, request.MaxStudents, request.Status);

        var actorId = _currentUser.GetRequiredUserId();
        var actorRole = _currentUser.GetRequiredRole();
        EnsureCanCreate(actorId, actorRole, request.TeacherId);

        var teacher = await _users.GetByIdAsync(request.TeacherId, cancellationToken)
            ?? throw new NotFoundException("Teacher was not found.");

        if (teacher.Role != Role.Teacher || teacher.Status != AccountStatus.Active)
        {
            throw new BadRequestException("Course teacher must be an active teacher account.");
        }

        var course = new Course(
            request.Name.Trim(),
            NormalizeOptional(request.Description),
            request.TeacherId,
            request.MaxStudents,
            request.Status,
            actorId);

        var chatRoom = ChatRoom.CreateForCourse(course.Id, actorId);
        chatRoom.AddCourseParticipant(actorId);
        if (request.TeacherId != actorId)
        {
            chatRoom.AddCourseParticipant(request.TeacherId);
        }
        chatRoom.SynchronizeWithCourse(course);

        _courses.Add(course);
        _chatRooms.Add(chatRoom);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(course, teacher.FullName);
    }

    public async Task<CourseDto> GetByIdAsync(
        Guid courseId,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");

        if (includeDeleted && !_currentUser.GetRequiredRole().IsAdminOrSystem())
        {
            throw new ForbiddenException("Only administrators can view deleted courses.");
        }

        var course = await _courses.GetByIdAsync(courseId, includeDeleted, cancellationToken)
            ?? throw new NotFoundException("Course was not found.");

        if (_currentUser.GetRequiredRole() == Role.Student &&
            !await CanStudentViewAsync(course, cancellationToken))
        {
            throw new NotFoundException("Course was not found.");
        }

        var acceptedCount = await _enrollments.CountAcceptedByCourseAsync(course.Id, cancellationToken);
        return await MapWithTeacherAsync(course, acceptedCount, cancellationToken);
    }

    private async Task<bool> CanStudentViewAsync(Course course, CancellationToken cancellationToken)
    {
        if (course.Status == CourseStatus.Open)
        {
            return true;
        }

        var enrollment = await _enrollments.GetByStudentAndCourseAsync(
            _currentUser.GetRequiredUserId(),
            course.Id,
            cancellationToken);
        return enrollment?.Status == EnrollmentStatus.Accepted;
    }

    public async Task<PagedResult<CourseDto>> ListAsync(
        CourseQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(query.Page, query.PageSize);

        if (query.Status is not null && !Enum.IsDefined(query.Status.Value))
        {
            throw new BadRequestException("Course status is invalid.");
        }

        if (query.IncludeDeleted && !_currentUser.GetRequiredRole().IsAdminOrSystem())
        {
            throw new ForbiddenException("Only administrators can view deleted courses.");
        }

        var search = NormalizeOptional(query.Search);
        var status = _currentUser.GetRequiredRole() == Role.Student
            ? CourseStatus.Open
            : query.Status;
        var skip = (query.Page - 1) * query.PageSize;
        var courses = await _courses.ListAsync(
            search,
            status,
            query.TeacherId,
            query.IncludeDeleted,
            skip,
            query.PageSize,
            cancellationToken);
        var totalCount = await _courses.CountAsync(
            search,
            status,
            query.TeacherId,
            query.IncludeDeleted,
            cancellationToken);
        var items = await MapManyAsync(courses, cancellationToken);

        return new PagedResult<CourseDto>(
            items,
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<IReadOnlyList<CourseDto>> ListMineAsync(
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.GetRequiredRole() != Role.Student)
        {
            throw new ForbiddenException("Only students can list their enrolled courses.");
        }

        var studentId = _currentUser.GetRequiredUserId();
        var enrollments = await _enrollments.ListByStudentAsync(studentId, cancellationToken);
        var courseIds = enrollments
            .Where(enrollment => enrollment.Status == EnrollmentStatus.Accepted)
            .Select(enrollment => enrollment.CourseId)
            .Distinct()
            .ToArray();
        if (courseIds.Length == 0)
        {
            return [];
        }

        var courses = await _courses.ListByIdsAsync(courseIds, cancellationToken);
        var visibleCourses = courses
            .Where(course => !course.IsDeleted)
            .OrderBy(course => course.Name)
            .ThenBy(course => course.Id)
            .ToArray();
        return await MapManyAsync(visibleCourses, cancellationToken);
    }

    public async Task<CourseDto> UpdateAsync(
        Guid courseId,
        UpdateCourseRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");
        ValidateCourse(request.Name, request.Description, request.MaxStudents, request.Status);

        CourseDto? result = null;
        async Task UpdateCore(CancellationToken transactionCancellationToken)
        {
            var course = await GetManagedCourseAsync(courseId, transactionCancellationToken);
            var acceptedCount = await _enrollments.CountAcceptedByCourseAsync(
                course.Id,
                transactionCancellationToken);
            if (request.MaxStudents < acceptedCount)
            {
                throw new ConflictException(
                    "Maximum students cannot be lower than the number of accepted enrollments.");
            }

            var actorId = _currentUser.GetRequiredUserId();
            course.SetName(request.Name.Trim());
            course.SetDescription(NormalizeOptional(request.Description));
            course.SetMaxStudents(request.MaxStudents);
            course.SetStatus(request.Status);
            course.SetModifiedBy(actorId);
            await SynchronizeChatRoomAsync(course, transactionCancellationToken);
            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
            result = await MapWithTeacherAsync(course, acceptedCount, transactionCancellationToken);
        }

        if (_transactionManager is null)
        {
            await UpdateCore(cancellationToken);
        }
        else
        {
            await _transactionManager.ExecuteSerializableAsync(UpdateCore, cancellationToken);
        }

        return result!;
    }

    public async Task DeleteAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");
        var course = await GetManagedCourseAsync(courseId, cancellationToken);
        course.Delete(_currentUser.GetRequiredUserId());
        await SynchronizeChatRoomAsync(course, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<CourseDto> RestoreAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");
        var course = await _courses.GetByIdAsync(courseId, true, cancellationToken)
            ?? throw new NotFoundException("Course was not found.");

        EnsureCanManage(course);
        course.Restore();
        course.SetModifiedBy(_currentUser.GetRequiredUserId());
        await SynchronizeChatRoomAsync(course, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        var acceptedCount = await _enrollments.CountAcceptedByCourseAsync(course.Id, cancellationToken);
        return await MapWithTeacherAsync(course, acceptedCount, cancellationToken);
    }

    private async Task<Course> GetManagedCourseAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var course = await _courses.GetByIdAsync(courseId, false, cancellationToken)
            ?? throw new NotFoundException("Course was not found.");
        EnsureCanManage(course);
        return course;
    }

    private void EnsureCanManage(Course course)
    {
        CourseAuthorization.EnsureCanManage(_currentUser, course);
    }

    private async Task SynchronizeChatRoomAsync(Course course, CancellationToken cancellationToken)
    {
        var room = await _chatRooms.GetByCourseIdAsync(course.Id, cancellationToken);
        room?.SynchronizeWithCourse(course);
    }

    private static void EnsureCanCreate(Guid actorId, Role actorRole, Guid teacherId)
    {
        if (actorRole.IsAdminOrSystem())
        {
            return;
        }

        if (actorRole != Role.Teacher || actorId != teacherId)
        {
            throw new ForbiddenException("Teachers can only create courses assigned to themselves.");
        }
    }

    private static void ValidateCourse(
        string name,
        string? description,
        int maxStudents,
        CourseStatus status)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            throw new BadRequestException("Course name must contain between 1 and 200 characters.");
        }

        if (description?.Trim().Length > 2000)
        {
            throw new BadRequestException("Course description cannot exceed 2000 characters.");
        }

        if (maxStudents <= 0)
        {
            throw new BadRequestException("Maximum students must be greater than zero.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new BadRequestException("Course status is invalid.");
        }
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page <= 0 || pageSize <= 0 || pageSize > MaximumPageSize)
        {
            throw new BadRequestException($"Page must be positive and page size must be between 1 and {MaximumPageSize}.");
        }
    }

    private static void ValidateId(Guid id, string fieldName)
    {
        if (id == Guid.Empty)
        {
            throw new BadRequestException($"{fieldName} is required.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<CourseDto[]> MapManyAsync(
        IReadOnlyCollection<Course> courses,
        CancellationToken cancellationToken)
    {
        if (courses.Count == 0)
        {
            return [];
        }

        var acceptedCounts = await _enrollments.CountAcceptedByCoursesAsync(
            courses.Select(course => course.Id).ToArray(),
            cancellationToken);
        var teachers = await _users.ListByIdsAsync(
            courses.Select(course => course.TeacherId).Distinct().ToArray(),
            cancellationToken);
        var teachersById = teachers.ToDictionary(teacher => teacher.Id);
        return courses.Select(course =>
        {
            if (!teachersById.TryGetValue(course.TeacherId, out var teacher))
            {
                throw new NotFoundException("Course teacher was not found.");
            }

            return Map(course, teacher.FullName, acceptedCounts.GetValueOrDefault(course.Id));
        }).ToArray();
    }

    private async Task<CourseDto> MapWithTeacherAsync(
        Course course,
        int acceptedStudentCount,
        CancellationToken cancellationToken)
    {
        var teacher = await _users.GetByIdAsync(course.TeacherId, cancellationToken)
            ?? throw new NotFoundException("Course teacher was not found.");
        return Map(course, teacher.FullName, acceptedStudentCount);
    }

    private static CourseDto Map(Course course, string teacherFullName, int acceptedStudentCount = 0) => new(
        course.Id,
        course.Name,
        course.Description,
        course.TeacherId,
        teacherFullName,
        course.MaxStudents,
        acceptedStudentCount,
        course.Status,
        course.CreatedAt,
        course.ModifiedAt,
        course.DeletedAt);
}

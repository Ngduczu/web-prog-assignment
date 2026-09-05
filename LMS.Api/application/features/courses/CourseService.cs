using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.common;
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
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IChatRoomRepository _chatRooms;

    public CourseService(
        ICourseRepository courses,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IChatRoomRepository chatRooms)
    {
        _courses = courses;
        _users = users;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _chatRooms = chatRooms;
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

        _courses.Add(course);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(course);
    }

    public async Task<CourseDto> GetByIdAsync(
        Guid courseId,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");

        if (includeDeleted && _currentUser.GetRequiredRole() != Role.Admin)
        {
            throw new ForbiddenException("Only administrators can view deleted courses.");
        }

        var course = await _courses.GetByIdAsync(courseId, includeDeleted, cancellationToken)
            ?? throw new NotFoundException("Course was not found.");

        return Map(course);
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

        if (query.IncludeDeleted && _currentUser.GetRequiredRole() != Role.Admin)
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

        return new PagedResult<CourseDto>(
            courses.Select(Map).ToArray(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public async Task<CourseDto> UpdateAsync(
        Guid courseId,
        UpdateCourseRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");
        ValidateCourse(request.Name, request.Description, request.MaxStudents, request.Status);

        var course = await GetManagedCourseAsync(courseId, cancellationToken);
        var actorId = _currentUser.GetRequiredUserId();

        course.SetName(request.Name.Trim());
        course.SetDescription(NormalizeOptional(request.Description));
        course.SetMaxStudents(request.MaxStudents);
        course.SetStatus(request.Status);
        course.SetModifiedBy(actorId);
        await SynchronizeChatRoomAsync(course, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(course);
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
        return Map(course);
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
        if (actorRole == Role.Admin)
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

    private static CourseDto Map(Course course) => new(
        course.Id,
        course.Name,
        course.Description,
        course.TeacherId,
        course.MaxStudents,
        course.Status,
        course.CreatedAt,
        course.ModifiedAt,
        course.DeletedAt);
}

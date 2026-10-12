using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.common;
using LMS.Api.application.features.courses;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Api.application.features.enrollments;

public sealed class EnrollmentService : IEnrollmentService
{
    private const int MaximumPageSize = 100;
    private const int MaximumBulkEmails = 100;
    private readonly IEnrollmentRepository _enrollments;
    private readonly ICourseRepository _courses;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionManager? _transactionManager;
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository? _users;

    public EnrollmentService(
        IEnrollmentRepository enrollments,
        ICourseRepository courses,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ITransactionManager? transactionManager = null,
        IUserRepository? users = null)
    {
        _users = users;
        _enrollments = enrollments;
        _courses = courses;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _transactionManager = transactionManager;
    }

    public async Task<EnrollmentDto> RequestAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");
        var studentId = _currentUser.GetRequiredUserId();

        if (_currentUser.GetRequiredRole() != Role.Student)
        {
            throw new ForbiddenException("Only students can request enrollment.");
        }

        var course = await _courses.GetByIdAsync(courseId, false, cancellationToken)
            ?? throw new NotFoundException("Course was not found.");

        if (course.Status != CourseStatus.Open)
        {
            throw new ConflictException("Enrollment is only available for open courses.");
        }

        var existing = await _enrollments.GetByStudentAndCourseAsync(studentId, courseId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status is EnrollmentStatus.Rejected or EnrollmentStatus.Cancelled)
            {
                existing.RequestAgain();
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Map(existing, course);
            }

            throw new ConflictException("An active enrollment for this student and course already exists.");
        }

        var enrollment = new Enrollment(studentId, courseId);
        _enrollments.Add(enrollment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(enrollment, course);
    }

    public async Task<IReadOnlyList<AddStudentResultDto>> AddStudentsByEmailAsync(
        Guid courseId,
        AddStudentsByEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");
        if (request.Emails is null || request.Emails.Count == 0 || request.Emails.Count > MaximumBulkEmails)
        {
            throw new BadRequestException($"Provide between 1 and {MaximumBulkEmails} email addresses.");
        }

        if (_transactionManager is null)
        {
            return await AddStudentsByEmailCoreAsync(courseId, request.Emails, cancellationToken);
        }

        IReadOnlyList<AddStudentResultDto> results = [];
        await _transactionManager.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            results = await AddStudentsByEmailCoreAsync(courseId, request.Emails, transactionCancellationToken);
        }, cancellationToken);
        return results;
    }

    private async Task<IReadOnlyList<AddStudentResultDto>> AddStudentsByEmailCoreAsync(
        Guid courseId,
        IReadOnlyList<string> emails,
        CancellationToken cancellationToken)
    {
        var users = _users ?? throw new InvalidOperationException("User repository is not available.");
        var course = await GetManagedCourseAsync(courseId, cancellationToken);
        if (course.Status != CourseStatus.Open)
        {
            throw new ConflictException("Students can only be added to open courses.");
        }

        var acceptedCount = await _enrollments.CountAcceptedByCourseAsync(course.Id, cancellationToken);
        var results = new List<AddStudentResultDto>(emails.Count);
        var processed = new HashSet<string>();
        foreach (var raw in emails)
        {
            var email = raw?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!IsValidEmail(email))
            {
                results.Add(new AddStudentResultDto(raw?.Trim() ?? string.Empty, AddStudentResultStatus.InvalidEmail, null));
                continue;
            }

            if (!processed.Add(email))
            {
                continue;
            }

            var user = await users.GetByEmailAsync(email, cancellationToken);
            if (user is null)
            {
                results.Add(new AddStudentResultDto(email, AddStudentResultStatus.NotFound, null));
                continue;
            }

            if (user.Role != Role.Student)
            {
                results.Add(new AddStudentResultDto(email, AddStudentResultStatus.NotStudent, null));
                continue;
            }

            if (user.Status != AccountStatus.Active)
            {
                results.Add(new AddStudentResultDto(email, AddStudentResultStatus.Inactive, null));
                continue;
            }

            var existing = await _enrollments.GetByStudentAndCourseAsync(user.Id, course.Id, cancellationToken);
            if (existing?.Status == EnrollmentStatus.Accepted)
            {
                results.Add(new AddStudentResultDto(email, AddStudentResultStatus.AlreadyAccepted, Map(existing, course)));
                continue;
            }

            if (acceptedCount >= course.MaxStudents)
            {
                results.Add(new AddStudentResultDto(email, AddStudentResultStatus.CourseFull, null));
                continue;
            }

            Enrollment enrollment;
            if (existing is null)
            {
                enrollment = new Enrollment(user.Id, course.Id, EnrollmentStatus.Accepted);
                _enrollments.Add(enrollment);
            }
            else
            {
                enrollment = existing;
                if (enrollment.Status != EnrollmentStatus.Waiting)
                {
                    enrollment.RequestAgain();
                }

                enrollment.Accept();
            }

            acceptedCount++;
            results.Add(new AddStudentResultDto(email, AddStudentResultStatus.Added, Map(enrollment, course)));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return results;
    }

    private static bool IsValidEmail(string email) =>
        email.Length is > 0 and <= 255 &&
        System.Net.Mail.MailAddress.TryCreate(email, out var address) &&
        address.Address == email;

    public async Task<PagedResult<EnrollmentDto>> ListByCourseAsync(
        Guid courseId,
        EnrollmentQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateId(courseId, "Course id");
        ValidatePage(query.Page, query.PageSize);
        var course = await GetManagedCourseAsync(courseId, cancellationToken);
        var skip = (query.Page - 1) * query.PageSize;
        var enrollments = await _enrollments.ListByCourseAsync(
            course.Id,
            skip,
            query.PageSize,
            cancellationToken);
        var totalCount = await _enrollments.CountByCourseAsync(course.Id, cancellationToken);

        return new PagedResult<EnrollmentDto>(
            enrollments.Select(enrollment => Map(enrollment, course)).ToArray(),
            query.Page,
            query.PageSize,
            totalCount);
    }

    public Task<EnrollmentDto> AcceptAsync(
        Guid enrollmentId,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(enrollmentId, EnrollmentStatus.Accepted, cancellationToken);

    public Task<EnrollmentDto> RejectAsync(
        Guid enrollmentId,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(enrollmentId, EnrollmentStatus.Rejected, cancellationToken);

    public async Task<IReadOnlyList<EnrollmentDto>> ListMineAsync(
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.GetRequiredRole() != Role.Student)
        {
            throw new ForbiddenException("Only students can list their enrollments.");
        }

        var items = await _enrollments.ListByStudentAsync(
            _currentUser.GetRequiredUserId(),
            cancellationToken);
        var courseIds = items.Select(enrollment => enrollment.CourseId).Distinct().ToArray();
        IReadOnlyList<Course> courses = courseIds.Length == 0
            ? Array.Empty<Course>()
            : await _courses.ListByIdsAsync(courseIds, cancellationToken);
        var coursesById = courses.ToDictionary(course => course.Id);
        return items
            .Select(enrollment => Map(enrollment, coursesById.GetValueOrDefault(enrollment.CourseId)))
            .ToArray();
    }

    public async Task<EnrollmentDto> CancelAsync(
        Guid enrollmentId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(enrollmentId, "Enrollment id");
        var enrollment = await _enrollments.GetByIdAsync(enrollmentId, cancellationToken)
            ?? throw new NotFoundException("Enrollment was not found.");

        if (_currentUser.GetRequiredRole() != Role.Student ||
            enrollment.StudentId != _currentUser.GetRequiredUserId())
        {
            throw new ForbiddenException("Students can only cancel their own enrollment requests.");
        }

        enrollment.Cancel();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(enrollment);
    }

    public async Task<EnrollmentDto> RevokeAsync(
        Guid enrollmentId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(enrollmentId, "Enrollment id");
        var enrollment = await _enrollments.GetByIdAsync(enrollmentId, cancellationToken)
            ?? throw new NotFoundException("Enrollment was not found.");
        var course = await GetManagedCourseAsync(enrollment.CourseId, cancellationToken);
        if (enrollment.Status != EnrollmentStatus.Accepted)
        {
            throw new ConflictException("Only accepted enrollments can be revoked.");
        }

        enrollment.Cancel();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(enrollment, course);
    }

    private async Task<EnrollmentDto> TransitionAsync(
        Guid enrollmentId,
        EnrollmentStatus targetStatus,
        CancellationToken cancellationToken)
    {
        if (_transactionManager is null)
        {
            return await TransitionCoreAsync(enrollmentId, targetStatus, cancellationToken);
        }

        EnrollmentDto? result = null;
        await _transactionManager.ExecuteSerializableAsync(async transactionCancellationToken =>
        {
            result = await TransitionCoreAsync(enrollmentId, targetStatus, transactionCancellationToken);
        }, cancellationToken);
        return result!;
    }

    private async Task<EnrollmentDto> TransitionCoreAsync(
        Guid enrollmentId,
        EnrollmentStatus targetStatus,
        CancellationToken cancellationToken)
    {
        ValidateId(enrollmentId, "Enrollment id");
        var enrollment = await _enrollments.GetByIdAsync(enrollmentId, cancellationToken)
            ?? throw new NotFoundException("Enrollment was not found.");
        var course = await GetManagedCourseAsync(enrollment.CourseId, cancellationToken);
        EnsureWaiting(enrollment);

        if (targetStatus == EnrollmentStatus.Accepted)
        {
            if (course.Status != CourseStatus.Open)
            {
                throw new ConflictException("Enrollments can only be accepted for open courses.");
            }

            var acceptedCount = await _enrollments.CountAcceptedByCourseAsync(course.Id, cancellationToken);
            if (acceptedCount >= course.MaxStudents)
            {
                throw new ConflictException("Course has reached its maximum number of students.");
            }

            enrollment.Accept();
        }
        else
        {
            enrollment.Reject();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(enrollment, course);
    }

    private async Task<Course> GetManagedCourseAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var course = await _courses.GetByIdAsync(courseId, false, cancellationToken)
            ?? throw new NotFoundException("Course was not found.");
        CourseAuthorization.EnsureCanManage(_currentUser, course);
        return course;
    }

    private static void EnsureWaiting(Enrollment enrollment)
    {
        if (enrollment.Status != EnrollmentStatus.Waiting)
        {
            throw new ConflictException("Only waiting enrollments can change status.");
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

    private static EnrollmentDto Map(Enrollment enrollment, Course? course = null) => new(
        enrollment.Id,
        enrollment.StudentId,
        enrollment.CourseId,
        enrollment.Status,
        enrollment.Student is null
            ? null
            : new EnrollmentStudentDto(
                enrollment.Student.Id,
                enrollment.Student.FullName,
                enrollment.Student.Email),
        enrollment.CreatedAt,
        enrollment.ModifiedAt,
        course is null ? null : new EnrollmentCourseDto(course.Id, course.Name, course.Status));
}

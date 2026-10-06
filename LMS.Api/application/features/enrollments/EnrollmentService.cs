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
    private readonly IEnrollmentRepository _enrollments;
    private readonly ICourseRepository _courses;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionManager? _transactionManager;
    private readonly ICurrentUser _currentUser;

    public EnrollmentService(
        IEnrollmentRepository enrollments,
        ICourseRepository courses,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ITransactionManager? transactionManager = null)
    {
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
                return Map(existing);
            }

            throw new ConflictException("An active enrollment for this student and course already exists.");
        }

        var enrollment = new Enrollment(studentId, courseId);
        _enrollments.Add(enrollment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(enrollment);
    }

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
            enrollments.Select(Map).ToArray(),
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
        return items.Select(Map).ToArray();
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
        _ = await GetManagedCourseAsync(enrollment.CourseId, cancellationToken);
        if (enrollment.Status != EnrollmentStatus.Accepted)
        {
            throw new ConflictException("Only accepted enrollments can be revoked.");
        }

        enrollment.Cancel();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(enrollment);
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
        return Map(enrollment);
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

    private static EnrollmentDto Map(Enrollment enrollment) => new(
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
        enrollment.ModifiedAt);
}

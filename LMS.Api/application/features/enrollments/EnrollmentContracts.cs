using LMS.Api.application.common;
using LMS.Api.domain.Enums.Courses;

namespace LMS.Api.application.features.enrollments;

public sealed record EnrollmentQuery(int Page = 1, int PageSize = 20);
public sealed record EnrollmentStudentDto(Guid Id, string FullName, string Email);
public sealed record EnrollmentCourseDto(Guid Id, string Name, CourseStatus Status);

public sealed record EnrollmentDto(
    Guid Id,
    Guid StudentId,
    Guid CourseId,
    EnrollmentStatus Status,
    EnrollmentStudentDto? Student,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    EnrollmentCourseDto? Course = null);

public sealed record AddStudentsByEmailRequest(IReadOnlyList<string>? Emails);

public enum AddStudentResultStatus
{
    Added,
    AlreadyAccepted,
    InvalidEmail,
    NotFound,
    NotStudent,
    Inactive,
    CourseFull,
}

public sealed record AddStudentResultDto(string Email, AddStudentResultStatus Status, EnrollmentDto? Enrollment);

public interface IEnrollmentService
{
    Task<IReadOnlyList<AddStudentResultDto>> AddStudentsByEmailAsync(
        Guid courseId,
        AddStudentsByEmailRequest request,
        CancellationToken cancellationToken = default);
    Task<EnrollmentDto> RequestAsync(Guid courseId, CancellationToken cancellationToken = default);
    Task<PagedResult<EnrollmentDto>> ListByCourseAsync(
        Guid courseId,
        EnrollmentQuery query,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EnrollmentDto>> ListMineAsync(CancellationToken cancellationToken = default);
    Task<EnrollmentDto> AcceptAsync(Guid enrollmentId, CancellationToken cancellationToken = default);
    Task<EnrollmentDto> RejectAsync(Guid enrollmentId, CancellationToken cancellationToken = default);
    Task<EnrollmentDto> CancelAsync(Guid enrollmentId, CancellationToken cancellationToken = default);
    Task<EnrollmentDto> RevokeAsync(Guid enrollmentId, CancellationToken cancellationToken = default);
}

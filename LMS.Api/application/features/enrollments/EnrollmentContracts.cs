using LMS.Api.application.common;
using LMS.Api.domain.Enums.Courses;

namespace LMS.Api.application.features.enrollments;

public sealed record EnrollmentQuery(int Page = 1, int PageSize = 20);

public sealed record EnrollmentDto(
    Guid Id,
    Guid StudentId,
    Guid CourseId,
    EnrollmentStatus Status,
    DateTime CreatedAt,
    DateTime ModifiedAt);

public interface IEnrollmentService
{
    Task<EnrollmentDto> RequestAsync(Guid courseId, CancellationToken cancellationToken = default);
    Task<PagedResult<EnrollmentDto>> ListByCourseAsync(
        Guid courseId,
        EnrollmentQuery query,
        CancellationToken cancellationToken = default);
    Task<EnrollmentDto> AcceptAsync(Guid enrollmentId, CancellationToken cancellationToken = default);
    Task<EnrollmentDto> RejectAsync(Guid enrollmentId, CancellationToken cancellationToken = default);
    Task<EnrollmentDto> CancelAsync(Guid enrollmentId, CancellationToken cancellationToken = default);
}

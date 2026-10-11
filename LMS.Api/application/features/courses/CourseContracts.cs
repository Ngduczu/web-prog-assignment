using LMS.Api.application.common;
using LMS.Api.domain.Enums.Courses;

namespace LMS.Api.application.features.courses;

public sealed record CreateCourseRequest(
    string Name,
    string? Description,
    Guid TeacherId,
    int MaxStudents,
    CourseStatus Status = CourseStatus.Open);

public sealed record UpdateCourseRequest(
    string Name,
    string? Description,
    int MaxStudents,
    CourseStatus Status);

public sealed record CourseQuery(
    string? Search = null,
    CourseStatus? Status = null,
    Guid? TeacherId = null,
    bool IncludeDeleted = false,
    int Page = 1,
    int PageSize = 20);

public sealed record CourseDto(
    Guid Id,
    string Name,
    string? Description,
    Guid TeacherId,
    int MaxStudents,
    int AcceptedStudentCount,
    CourseStatus Status,
    DateTime CreatedAt,
    DateTime ModifiedAt,
    DateTime? DeletedAt);

public interface ICourseService
{
    Task<CourseDto> CreateAsync(CreateCourseRequest request, CancellationToken cancellationToken = default);
    Task<CourseDto> GetByIdAsync(Guid courseId, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task<PagedResult<CourseDto>> ListAsync(CourseQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CourseDto>> ListMineAsync(CancellationToken cancellationToken = default);
    Task<CourseDto> UpdateAsync(Guid courseId, UpdateCourseRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid courseId, CancellationToken cancellationToken = default);
    Task<CourseDto> RestoreAsync(Guid courseId, CancellationToken cancellationToken = default);
}

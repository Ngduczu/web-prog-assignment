using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Courses;

namespace LMS.Api.domain.repositories;

public interface ICourseRepository
{
    Task<Course?> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken cancellationToken);
    Task<IReadOnlyList<Course>> ListAsync(
        string? search,
        CourseStatus? status,
        Guid? teacherId,
        bool includeDeleted,
        int skip,
        int take,
        CancellationToken cancellationToken);
    Task<int> CountAsync(
        string? search,
        CourseStatus? status,
        Guid? teacherId,
        bool includeDeleted,
        CancellationToken cancellationToken);
    void Add(Course course);
}

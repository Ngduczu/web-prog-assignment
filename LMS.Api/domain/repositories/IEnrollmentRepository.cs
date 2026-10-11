using LMS.Api.domain.Entities.Courses;

namespace LMS.Api.domain.repositories;

public interface IEnrollmentRepository
{
    Task<Enrollment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Enrollment?> GetByStudentAndCourseAsync(
        Guid studentId,
        Guid courseId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Enrollment>> ListByStudentAsync(
        Guid studentId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Enrollment>> ListByCourseAsync(
        Guid courseId,
        int skip,
        int take,
        CancellationToken cancellationToken);
    Task<int> CountByCourseAsync(Guid courseId, CancellationToken cancellationToken);
    Task<int> CountAcceptedByCourseAsync(Guid courseId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, int>> CountAcceptedByCoursesAsync(
        IReadOnlyCollection<Guid> courseIds,
        CancellationToken cancellationToken);
    void Add(Enrollment enrollment);
}

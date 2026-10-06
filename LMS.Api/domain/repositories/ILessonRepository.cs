using LMS.Api.domain.Entities.Courses;

namespace LMS.Api.domain.repositories;

public interface ILessonRepository
{
    Task<Lesson?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Lesson>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken);
    Task<bool> OrderExistsAsync(Guid courseId, int order, Guid? excludingLessonId, CancellationToken cancellationToken);
    void Add(Lesson lesson);
    void Remove(Lesson lesson) => throw new NotSupportedException();
}

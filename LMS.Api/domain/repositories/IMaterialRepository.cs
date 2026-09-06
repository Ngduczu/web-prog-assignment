using LMS.Api.domain.Entities.Courses;

namespace LMS.Api.domain.repositories;

public interface IMaterialRepository
{
    Task<Material?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Material>> ListByLessonAsync(Guid lessonId, CancellationToken cancellationToken);
    void Add(Material material);
    void Remove(Material material);
}

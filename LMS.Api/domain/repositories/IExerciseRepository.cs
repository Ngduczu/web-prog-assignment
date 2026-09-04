using LMS.Api.domain.Entities.Courses;

namespace LMS.Api.domain.repositories;

public interface IExerciseRepository
{
    Task<Exercise?> GetByIdAsync(Guid id, bool includeAttachment, CancellationToken cancellationToken);
    Task<IReadOnlyList<Exercise>> ListByCourseAsync(Guid courseId, CancellationToken cancellationToken);
    void Add(Exercise exercise);
    void AddAttachment(ExerciseAttachment attachment);
}

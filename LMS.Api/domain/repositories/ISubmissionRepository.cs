using LMS.Api.domain.Entities.Courses;

namespace LMS.Api.domain.repositories;

public interface ISubmissionRepository
{
    Task<Submission?> GetByIdAsync(Guid id, bool includeAttachment, CancellationToken cancellationToken);
    Task<Submission?> GetByExerciseAndStudentAsync(
        Guid exerciseId,
        Guid studentId,
        bool includeAttachment,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Submission>> ListSubmittedByExerciseAsync(
        Guid exerciseId,
        int skip,
        int take,
        CancellationToken cancellationToken);
    Task<int> CountSubmittedByExerciseAsync(Guid exerciseId, CancellationToken cancellationToken);
    void Add(Submission submission);
    void AddAttachment(SubmissionAttachment attachment);
}

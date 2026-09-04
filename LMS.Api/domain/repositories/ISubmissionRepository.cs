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
    void Add(Submission submission);
    void AddAttachment(SubmissionAttachment attachment);
}
